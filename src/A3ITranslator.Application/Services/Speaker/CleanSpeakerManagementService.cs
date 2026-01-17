using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.DTOs.Translation;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services.Speaker;

/// <summary>
/// Unified Speaker Management Service - Manages the Master SpeakerProfile
/// Implements Flow C by merging acoustic and linguistic signals.
/// </summary>
public interface ISpeakerManagementService
{
    Task<SpeakerOperationResult> ProcessSpeakerIdentificationAsync(string sessionId, SpeakerServicePayload payload);
    List<SpeakerProfile> GetSessionSpeakers(string sessionId);
    string BuildSpeakerPromptContext(string sessionId);
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
    private readonly Dictionary<string, List<SpeakerProfile>> _sessionSpeakers = new();
    private readonly object _lock = new();

    public SpeakerManagementService(ILogger<SpeakerManagementService> logger)
    {
        _logger = logger;
    }

    public async Task<SpeakerOperationResult> ProcessSpeakerIdentificationAsync(string sessionId, SpeakerServicePayload payload)
    {
        await Task.CompletedTask;
        try
        {
            lock (_lock)
            {
                var speakers = GetSessionSpeakersInternal(sessionId);
                var identification = payload.Identification;

                return identification.Decision switch
                {
                    "CONFIRMED_EXISTING" => HandleExistingSpeaker(sessionId, speakers, payload),
                    "NEW_SPEAKER" => HandleNewSpeaker(sessionId, speakers, payload),
                    _ => HandleUncertainSpeaker(sessionId, speakers, payload)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process speaker identification for session {SessionId}", sessionId);
            return new SpeakerOperationResult { Success = false, ErrorMessage = ex.Message, Action = "Failed" };
        }
    }

    public List<SpeakerProfile> GetSessionSpeakers(string sessionId)
    {
        lock (_lock) { return GetSessionSpeakersInternal(sessionId); }
    }

    public string BuildSpeakerPromptContext(string sessionId)
    {
        var speakers = GetSessionSpeakers(sessionId);
        if (!speakers.Any()) return "**No known speakers in this session yet.**";

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
        lock (_lock) { _sessionSpeakers.Remove(sessionId); }
        await Task.CompletedTask;
    }

    private List<SpeakerProfile> GetSessionSpeakersInternal(string sessionId)
    {
        if (!_sessionSpeakers.ContainsKey(sessionId)) _sessionSpeakers[sessionId] = new List<SpeakerProfile>();
        return _sessionSpeakers[sessionId];
    }

    private SpeakerOperationResult HandleExistingSpeaker(string sessionId, List<SpeakerProfile> speakers, SpeakerServicePayload payload)
    {
        var speakerId = payload.Identification.FinalSpeakerId;
        var speaker = speakers.FirstOrDefault(s => s.SpeakerId == speakerId);

        if (speaker != null)
        {
            speaker.AddUtterance(payload.AudioLanguage, payload.TranscriptionConfidence);
            UpdateProfileFromPayload(speaker, payload);
            return new SpeakerOperationResult { SpeakerId = speakerId!, Action = "Updated", Confidence = payload.Identification.Confidence };
        }
        return HandleNewSpeaker(sessionId, speakers, payload);
    }

    private SpeakerOperationResult HandleNewSpeaker(string sessionId, List<SpeakerProfile> speakers, SpeakerServicePayload payload)
    {
        var newSpeakerId = payload.Identification.FinalSpeakerId ?? $"speaker_{speakers.Count + 1}";
        var speaker = new SpeakerProfile
        {
            SpeakerId = newSpeakerId,
            DisplayName = payload.ProfileUpdate.SuggestedName ?? $"Speaker {speakers.Count + 1}"
        };

        speaker.AddUtterance(payload.AudioLanguage, payload.TranscriptionConfidence);
        UpdateProfileFromPayload(speaker, payload);
        
        speakers.Add(speaker);
        return new SpeakerOperationResult { SpeakerId = newSpeakerId, IsNewSpeaker = true, Action = "Created", Confidence = payload.Identification.Confidence };
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

    private SpeakerOperationResult HandleUncertainSpeaker(string sessionId, List<SpeakerProfile> speakers, SpeakerServicePayload payload)
    {
        if (payload.Identification.Confidence > 0.4f) return HandleNewSpeaker(sessionId, speakers, payload);
        return new SpeakerOperationResult { Success = false, SpeakerId = "uncertain", Action = "Skipped" };
    }
}
