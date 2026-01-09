using A3ITranslator.Application.Models.SpeakerProfiles;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.DTOs.Speaker;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services.Speaker;

/// <summary>
/// Interface for speaker profile management
/// </summary>
public interface ISpeakerProfileManager
{
    /// <summary>
    /// Get all speaker profiles for a session
    /// </summary>
    List<SpeakerProfile> GetSpeakerProfiles(string sessionId);

    /// <summary>
    /// Add new speaker to session
    /// </summary>
    void AddSpeaker(string sessionId, SpeakerProfile speaker);

    /// <summary>
    /// Update existing speaker profile
    /// </summary>
    void UpdateSpeaker(string sessionId, SpeakerProfile speaker);

    /// <summary>
    /// Process speaker decision result and update profiles
    /// </summary>
    SpeakerListUpdate ProcessSpeakerDecision(string sessionId, SpeakerDecisionResult decision);

    /// <summary>
    /// Get speaker list update if changes occurred
    /// </summary>
    SpeakerListUpdate? GetSpeakerListUpdate(string sessionId);

    /// <summary>
    /// Clear all speakers for a session (cleanup)
    /// </summary>
    void ClearSession(string sessionId);

    /// <summary>
    /// Check if speaker list has changed since last update
    /// </summary>
    bool HasChanges(string sessionId);
}

/// <summary>
/// Session-scoped speaker profile manager
/// Implements SOLID principles with clean separation of concerns
/// </summary>
public class SpeakerProfileManager : ISpeakerProfileManager
{
    private readonly ILogger<SpeakerProfileManager> _logger;
    
    // Session-scoped speaker storage
    private readonly Dictionary<string, List<SpeakerProfile>> _sessionSpeakers = new();
    private readonly Dictionary<string, bool> _sessionChanges = new();
    private readonly object _lock = new object();

    public SpeakerProfileManager(ILogger<SpeakerProfileManager> logger)
    {
        _logger = logger;
    }

    public List<SpeakerProfile> GetSpeakerProfiles(string sessionId)
    {
        lock (_lock)
        {
            return _sessionSpeakers.GetValueOrDefault(sessionId, new List<SpeakerProfile>());
        }
    }

    public void AddSpeaker(string sessionId, SpeakerProfile speaker)
    {
        lock (_lock)
        {
            if (!_sessionSpeakers.ContainsKey(sessionId))
            {
                _sessionSpeakers[sessionId] = new List<SpeakerProfile>();
            }

            _sessionSpeakers[sessionId].Add(speaker);
            _sessionChanges[sessionId] = true;

            _logger.LogInformation("Added new speaker {SpeakerId} ({DisplayName}) to session {SessionId}",
                speaker.SpeakerId, speaker.DisplayName, sessionId);
        }
    }

    public void UpdateSpeaker(string sessionId, SpeakerProfile updatedSpeaker)
    {
        lock (_lock)
        {
            var speakers = _sessionSpeakers.GetValueOrDefault(sessionId, new List<SpeakerProfile>());
            var existingIndex = speakers.FindIndex(s => s.SpeakerId == updatedSpeaker.SpeakerId);

            if (existingIndex >= 0)
            {
                var oldSpeaker = speakers[existingIndex];
                speakers[existingIndex] = updatedSpeaker;
                _sessionChanges[sessionId] = true;

                _logger.LogInformation("Updated speaker {SpeakerId} in session {SessionId}: {OldUtterances} -> {NewUtterances} utterances",
                    updatedSpeaker.SpeakerId, sessionId, oldSpeaker.TotalUtterances, updatedSpeaker.TotalUtterances);
            }
            else
            {
                _logger.LogWarning("Attempted to update non-existent speaker {SpeakerId} in session {SessionId}",
                    updatedSpeaker.SpeakerId, sessionId);
            }
        }
    }

    public SpeakerListUpdate ProcessSpeakerDecision(string sessionId, SpeakerDecisionResult decision)
    {
        var hasChanges = false;

        switch (decision.Action)
        {
            case SpeakerDecisionAction.ConfirmExistingSpeaker:
                if (decision.MatchedSpeaker != null)
                {
                    // Just update last active time, no structural changes
                    decision.MatchedSpeaker.LastActive = DateTime.UtcNow;
                    hasChanges = false; 
                }
                break;

            case SpeakerDecisionAction.UpdateExistingSpeaker:
                if (decision.MatchedSpeaker != null)
                {
                    UpdateSpeaker(sessionId, decision.MatchedSpeaker);
                    hasChanges = true;
                }
                break;

            case SpeakerDecisionAction.CreateNewSpeaker:
                if (decision.NewSpeaker != null)
                {
                    AddSpeaker(sessionId, decision.NewSpeaker);
                    hasChanges = true;
                }
                break;

            case SpeakerDecisionAction.AwaitMoreData:
                hasChanges = false;
                break;
        }

        return new SpeakerListUpdate
        {
            Speakers = hasChanges ? GetEnhancedSpeakerList(sessionId, decision) : new List<EnhancedSpeakerInfo>(),
            HasChanges = hasChanges,
            ActiveSpeakerId = GetActiveSpeakerId(decision),
            ChangeType = DetermineChangeType(decision)
        };
    }

    public SpeakerListUpdate? GetSpeakerListUpdate(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionChanges.GetValueOrDefault(sessionId, false))
            {
                _sessionChanges[sessionId] = false; // Reset flag
                
                return new SpeakerListUpdate
                {
                    Speakers = GetSpeakerListSummary(sessionId),
                    HasChanges = true,
                    ActiveSpeakerId = null,
                    ChangeType = SpeakerChangeType.UpdatedProfile
                };
            }

            return null;
        }
    }

    public void ClearSession(string sessionId)
    {
        lock (_lock)
        {
            if (_sessionSpeakers.ContainsKey(sessionId))
            {
                var speakerCount = _sessionSpeakers[sessionId].Count;
                _sessionSpeakers.Remove(sessionId);
                _sessionChanges.Remove(sessionId);

                _logger.LogInformation("Cleared {SpeakerCount} speakers for session {SessionId}",
                    speakerCount, sessionId);
            }
        }
    }

    public bool HasChanges(string sessionId)
    {
        lock (_lock)
        {
            return _sessionChanges.GetValueOrDefault(sessionId, false);
        }
    }

    /// <summary>
    /// Get enhanced speaker list with confidence metrics
    /// </summary>
    private List<EnhancedSpeakerInfo> GetEnhancedSpeakerList(string sessionId, SpeakerDecisionResult? decision)
    {
        lock (_lock)
        {
            if (!_sessionSpeakers.ContainsKey(sessionId))
                return new List<EnhancedSpeakerInfo>();

            var speakers = _sessionSpeakers[sessionId];
            return speakers.Select(speaker => 
            {
                var enhancedInfo = EnhancedSpeakerInfo.FromDomainModel(speaker);
                
                // Set additional context from decision
                if (decision?.MatchedSpeaker?.SpeakerId == speaker.SpeakerId)
                {
                    enhancedInfo.IdentificationConfidence = decision.Confidence;
                }
                else if (decision?.NewSpeaker?.SpeakerId == speaker.SpeakerId)
                {
                    enhancedInfo.IsNewSpeaker = true;
                    enhancedInfo.IdentificationConfidence = decision.Confidence;
                }

                return enhancedInfo;
            }).ToList();
        }
    }

    /// <summary>
    /// Determine the type of speaker change that occurred
    /// </summary>
    private SpeakerChangeType DetermineChangeType(SpeakerDecisionResult decision)
    {
        return decision.Action switch
        {
            SpeakerDecisionAction.CreateNewSpeaker => SpeakerChangeType.NewSpeaker,
            SpeakerDecisionAction.ConfirmExistingSpeaker => SpeakerChangeType.ConfirmedSpeaker,
            SpeakerDecisionAction.UpdateExistingSpeaker => SpeakerChangeType.UpdatedProfile,
            _ => SpeakerChangeType.SpeakerSwitch
        };
    }

    /// <summary>
    /// Get the active speaker ID from decision result
    /// </summary>
    private string? GetActiveSpeakerId(SpeakerDecisionResult decision)
    {
        return decision.MatchedSpeaker?.SpeakerId ?? decision.NewSpeaker?.SpeakerId;
    }

    /// <summary>
    /// Legacy method for compatibility - now returns enhanced speaker info
    /// </summary>
    private List<EnhancedSpeakerInfo> GetSpeakerListSummary(string sessionId)
    {
        return GetEnhancedSpeakerList(sessionId, null);
    }
}
