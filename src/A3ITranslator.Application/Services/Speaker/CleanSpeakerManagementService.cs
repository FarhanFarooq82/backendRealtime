using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services.Speaker;

/// <summary>
/// Modern Speaker Management Service - Single Source of Truth using Session Repository
/// Implements Flow C by merging acoustic and linguistic signals.
/// No backward compatibility - pure async modern implementation.
/// </summary>
public interface ISpeakerManagementService
{
    Task<SpeakerOperationResult> ProcessSpeakerIdentificationAsync(string sessionId, SpeakerServicePayload payload);
    Task<List<SpeakerProfile>> GetSessionSpeakersAsync(string sessionId);
    Task<string> BuildSpeakerPromptContextAsync(string sessionId);
    Task ClearSessionAsync(string sessionId);
}

public class SpeakerOperationResult
{
    public bool Success { get; set; } = true;
    public string SpeakerId { get; set; } = string.Empty;
    public bool IsNewSpeaker { get; set; } = false;
    public float Confidence { get; set; } = 0f;
    public string Action { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class SpeakerManagementService : ISpeakerManagementService
{
    private readonly ILogger<SpeakerManagementService> _logger;
    private readonly ISessionRepository _sessionRepository;

    public SpeakerManagementService(
        ILogger<SpeakerManagementService> logger,
        ISessionRepository sessionRepository)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
    }

    public async Task<SpeakerOperationResult> ProcessSpeakerIdentificationAsync(string sessionId, SpeakerServicePayload payload)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            if (session == null)
            {
                _logger.LogWarning("⚠️ Session {SessionId} not found for speaker identification", sessionId);
                return new SpeakerOperationResult 
                { 
                    Success = false, 
                    ErrorMessage = "Session not found", 
                    Action = "Failed" 
                };
            }

            var speakers = session.Speakers.ToList();
            var identification = payload.Identification;

            var result = identification.Decision switch
            {
                "CONFIRMED_EXISTING" => await HandleExistingSpeakerAsync(session, speakers, payload),
                "NEW_SPEAKER" => await HandleNewSpeakerAsync(session, speakers, payload),
                _ => await HandleUncertainSpeakerAsync(session, speakers, payload)
            };

            // Save session after any speaker changes
            if (result.Success && (result.IsNewSpeaker || result.Action == "Updated"))
            {
                await _sessionRepository.SaveAsync(session);
                _logger.LogInformation("💾 Session {SessionId} saved with speaker changes", sessionId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process speaker identification for session {SessionId}", sessionId);
            return new SpeakerOperationResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message, 
                Action = "Failed" 
            };
        }
    }

    public async Task<List<SpeakerProfile>> GetSessionSpeakersAsync(string sessionId)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            return session?.Speakers.ToList() ?? new List<SpeakerProfile>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get speakers for session {SessionId}", sessionId);
            return new List<SpeakerProfile>();
        }
    }

    public async Task<string> BuildSpeakerPromptContextAsync(string sessionId)
    {
        var speakers = await GetSessionSpeakersAsync(sessionId);
        if (!speakers.Any()) 
            return "**No known speakers in this session yet.**";

        var context = new System.Text.StringBuilder();
        context.AppendLine("**Known Speakers in Session:**");
        foreach (var speaker in speakers.OrderBy(s => s.CreatedAt))
        {
            context.AppendLine($"- **{speaker.SpeakerId}**: {speaker.DisplayName}");
            context.AppendLine($"  - Style: {speaker.Insights.CommunicationStyle} ({speaker.Gender})");
            context.AppendLine($"  - Utterances: {speaker.TotalUtterances}");
            if (speaker.Insights.TypicalPhrases.Count > 0)
                context.AppendLine($"  - Typical phrases: {string.Join(", ", speaker.Insights.TypicalPhrases.Take(3))}");
            context.AppendLine();
        }
        return context.ToString().Trim();
    }

    public async Task ClearSessionAsync(string sessionId)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            if (session != null)
            {
                // Clear speakers from session - the speakers list is private, so we'd need a method on the session
                // For now, we'll just remove the session entirely which effectively clears all speakers
                await _sessionRepository.RemoveByConnectionIdAsync(session.ConnectionId);
                _logger.LogInformation("🧹 Session {SessionId} speakers cleared", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to clear session {SessionId}", sessionId);
        }
    }

    // --- Private Speaker Handling Methods ---

    private async Task<SpeakerOperationResult> HandleExistingSpeakerAsync(
        Domain.Entities.ConversationSession session, 
        List<SpeakerProfile> speakers, 
        SpeakerServicePayload payload)
    {
        var speakerId = payload.Identification.FinalSpeakerId;
        var speaker = speakers.FirstOrDefault(s => s.SpeakerId == speakerId);

        if (speaker != null)
        {
            speaker.AddUtterance(payload.AudioLanguage, payload.TranscriptionConfidence);
            UpdateProfileFromPayload(speaker, payload);
            
            _logger.LogInformation("🔄 Updated existing speaker {SpeakerId} in session {SessionId}", 
                speakerId, session.SessionId);
            
            return new SpeakerOperationResult 
            { 
                SpeakerId = speakerId!, 
                Action = "Updated", 
                Confidence = payload.Identification.Confidence 
            };
        }

        // Speaker not found, treat as new
        return await HandleNewSpeakerAsync(session, speakers, payload);
    }

    private Task<SpeakerOperationResult> HandleNewSpeakerAsync(
        Domain.Entities.ConversationSession session,
        List<SpeakerProfile> speakers, 
        SpeakerServicePayload payload)
    {
        var newSpeakerId = payload.Identification.FinalSpeakerId ?? $"speaker_{speakers.Count + 1}";
        var speaker = new SpeakerProfile
        {
            SpeakerId = newSpeakerId,
            DisplayName = payload.ProfileUpdate.SuggestedName ?? $"Speaker {speakers.Count + 1}"
        };

        speaker.AddUtterance(payload.AudioLanguage, payload.TranscriptionConfidence);
        UpdateProfileFromPayload(speaker, payload);
        
        // Add to session using domain method
        session.AddSpeaker(speaker);
        
        _logger.LogInformation("✨ Created new speaker {SpeakerId} ({DisplayName}) in session {SessionId}", 
            newSpeakerId, speaker.DisplayName, session.SessionId);
        
        return Task.FromResult(new SpeakerOperationResult 
        { 
            SpeakerId = newSpeakerId, 
            IsNewSpeaker = true, 
            Action = "Created", 
            Confidence = payload.Identification.Confidence 
        });
    }

    private Task<SpeakerOperationResult> HandleUncertainSpeakerAsync(
        Domain.Entities.ConversationSession session,
        List<SpeakerProfile> speakers, 
        SpeakerServicePayload payload)
    {
        // If confidence is reasonable, treat as new speaker
        if (payload.Identification.Confidence > 0.4f) 
        {
            return HandleNewSpeakerAsync(session, speakers, payload);
        }
        
        _logger.LogWarning("❓ Skipped uncertain speaker identification in session {SessionId} (confidence: {Confidence})", 
            session.SessionId, payload.Identification.Confidence);
        
        return Task.FromResult(new SpeakerOperationResult 
        { 
            Success = false, 
            SpeakerId = "uncertain", 
            Action = "Skipped",
            Confidence = payload.Identification.Confidence
        });
    }

    private void UpdateProfileFromPayload(SpeakerProfile speaker, SpeakerServicePayload payload)
    {
        var update = payload.ProfileUpdate;
        if (!string.IsNullOrEmpty(update.SuggestedName)) speaker.DisplayName = update.SuggestedName;
        if (!string.IsNullOrEmpty(update.EstimatedGender)) 
            speaker.Gender = Enum.TryParse<SpeakerGender>(update.EstimatedGender, true, out var g) ? g : speaker.Gender;
        
        speaker.Insights.CommunicationStyle = update.Tone ?? speaker.Insights.CommunicationStyle;
        speaker.Insights.TypicalPhrases = update.TypicalPhrases ?? speaker.Insights.TypicalPhrases;
        speaker.PreferredLanguage = update.PreferredLanguage ?? speaker.PreferredLanguage;
        speaker.Confidence = Math.Max(speaker.Confidence, update.ProfileConfidence);

        // 🚀 FEATURE SYNC: Update Acoustic DNA
        if (payload.AudioFingerprint != null)
        {
             if (speaker.VoiceFingerprint.AveragePitch == 0 && speaker.VoiceFingerprint.MfccVector.Length == 0)
             {
                 speaker.VoiceFingerprint = payload.AudioFingerprint;
             }
             else
             {
                 speaker.UpdateAcousticFeatures(payload.AudioFingerprint.AveragePitch, payload.AudioFingerprint.MfccVector);
             }
        }
    }
}
