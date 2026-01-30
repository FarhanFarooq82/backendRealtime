using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services.Speaker;

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
    public string DisplayName { get; set; } = string.Empty;
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
                return new SpeakerOperationResult { Success = false, ErrorMessage = "Session not found" };
            }

            var analysis = payload.TurnAnalysis;
            var activeId = analysis.ActiveSpeakerId;

            // 1. Sync the Roster (GenAI's perspective of participants)
            SyncRoster(session, payload.Roster);

            // 2. Handle Decision Type
            switch (analysis.DecisionType)
            {
                case "MERGE" when analysis.MergeDetails != null:
                    _logger.LogInformation("🤝 MERGE TRIGGERED: {Ghost} into {Target} for session {SessionId}", 
                        analysis.MergeDetails.GhostIdToRemove, analysis.MergeDetails.TargetIdToKeep, sessionId);
                    session.MergeSpeakers(analysis.MergeDetails.GhostIdToRemove, analysis.MergeDetails.TargetIdToKeep);
                    activeId = analysis.MergeDetails.TargetIdToKeep;
                    break;

                case "NEW":
                    _logger.LogInformation("✨ NEW SPEAKER CONFIRMED: {SpeakerId} in session {SessionId}", activeId, sessionId);
                    break;
            }

            // 3. Update the Active Speaker's DNA (Mathematics)
            var activeSpeaker = session.GetSpeaker(activeId);
            if (activeSpeaker != null)
            {
                activeSpeaker.AddUtterance(payload.AudioLanguage, payload.TranscriptionConfidence);
                
                if (payload.AudioFingerprint != null && payload.AudioFingerprint.Embedding.Length > 0)
                {
                    _logger.LogDebug("🧬 Updating Neural DNA for {SpeakerId}", activeId);
                    activeSpeaker.UpdateAcousticFeatures(payload.AudioFingerprint.Embedding);
                }
            }

            await _sessionRepository.SaveAsync(session);

            return new SpeakerOperationResult
            {
                SpeakerId = activeId,
                DisplayName = activeSpeaker?.DisplayName ?? "Unknown",
                Confidence = analysis.IdentificationConfidence,
                Action = analysis.DecisionType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process speaker identification for session {SessionId}", sessionId);
            return new SpeakerOperationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private void SyncRoster(Domain.Entities.ConversationSession session, List<RosterSpeakerProfile> roster)
    {
        foreach (var rosterSpeaker in roster)
        {
            var existing = session.GetSpeaker(rosterSpeaker.SpeakerId);
            if (existing == null)
            {
                // Add new speaker from roster
                var newProfile = new SpeakerProfile
                {
                    SpeakerId = rosterSpeaker.SpeakerId,
                    DisplayName = rosterSpeaker.DisplayName,
                    PreferredLanguage = rosterSpeaker.PreferredLanguage,
                    IsLocked = rosterSpeaker.IsLocked
                };
                newProfile.Insights.CommunicationStyle = rosterSpeaker.Tone;
                // Note: estimatedGender and socialRole would need fields in SpeakerInsight or SpeakerProfile
                session.AddSpeaker(newProfile);
            }
            else if (!existing.IsLocked)
            {
                // Update existing speaker if not locked
                existing.DisplayName = rosterSpeaker.DisplayName;
                existing.PreferredLanguage = rosterSpeaker.PreferredLanguage;
                existing.Insights.CommunicationStyle = rosterSpeaker.Tone;
                existing.IsLocked = rosterSpeaker.IsLocked;
            }
        }

        // Optional: Remove speakers NOT in the roster (Session Cleaning)
        // Disabled to prevent accidental data loss if GenAI response is incomplete
        /*
        var rosterIds = roster.Select(r => r.SpeakerId).ToHashSet();
        var ghosts = session.Speakers.Where(s => !rosterIds.Contains(s.SpeakerId)).ToList();
        foreach (var ghost in ghosts)
        {
            _logger.LogDebug("🧹 Removing speaker {SpeakerId} not present in GenAI roster", ghost.SpeakerId);
            session.RemoveSpeaker(ghost.SpeakerId);
        }
        */
    }

    public async Task<List<SpeakerProfile>> GetSessionSpeakersAsync(string sessionId)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        return session?.Speakers.ToList() ?? new List<SpeakerProfile>();
    }

    public async Task<string> BuildSpeakerPromptContextAsync(string sessionId)
    {
        var speakers = await GetSessionSpeakersAsync(sessionId);
        if (!speakers.Any()) return "None.";

        var sb = new System.Text.StringBuilder();
        foreach (var s in speakers)
        {
            sb.AppendLine($"- {s.SpeakerId}: {s.DisplayName} (Role: {s.Insights.AssignedRole}, Language: {s.PreferredLanguage}, Locked: {s.IsLocked})");
        }
        return sb.ToString();
    }

    public async Task ClearSessionAsync(string sessionId)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session != null) await _sessionRepository.RemoveByConnectionIdAsync(session.ConnectionId);
    }
}
