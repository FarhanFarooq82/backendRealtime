using A3ITranslator.Application.Models.SpeakerProfiles;
using A3ITranslator.Application.Models.Conversation;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services.Speaker;

public class SpeakerDecisionResult
{
    public SpeakerDecisionAction Action { get; set; }
    public SpeakerProfile? MatchedSpeaker { get; set; }
    public SpeakerProfile? NewSpeaker { get; set; }
    public float Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

public enum SpeakerDecisionAction
{
    ConfirmExistingSpeaker,
    UpdateExistingSpeaker, 
    CreateNewSpeaker,
    AwaitMoreData
}

public interface ISpeakerDecisionEngine
{
    SpeakerDecisionResult MakeDecision(
        UtteranceWithContext utterance,
        List<SpeakerProfile> existingSpeakers,
        SpeakerInsights? genAIInsights = null);
}

/// <summary>
/// Simplified decision engine focusing on "Identity Locking" and Linguistic DNA.
/// </summary>
public class SpeakerDecisionEngine : ISpeakerDecisionEngine
{
    private readonly ILogger<SpeakerDecisionEngine> _logger;
    private const float LOCK_THRESHOLD = 80f;
    private const float MATCH_THRESHOLD = 50f;

    public SpeakerDecisionEngine(ILogger<SpeakerDecisionEngine> logger)
    {
        _logger = logger;
    }

    public SpeakerDecisionResult MakeDecision(
        UtteranceWithContext utterance,
        List<SpeakerProfile> existingSpeakers,
        SpeakerInsights? genAIInsights = null)
    {
        // 1. Check if we have a "Locked" speaker from Audio Engine
        if (!string.IsNullOrEmpty(utterance.ProvisionalSpeakerId))
        {
            var matched = existingSpeakers.FirstOrDefault(s => s.SpeakerId == utterance.ProvisionalSpeakerId);
            if (matched != null && matched.IsLocked)
            {
                return new SpeakerDecisionResult
                {
                    Action = SpeakerDecisionAction.ConfirmExistingSpeaker,
                    MatchedSpeaker = matched,
                    Confidence = 95f,
                    Reasoning = "Identity locked by physical audio fingerprint."
                };
            }
        }

        // 2. Try matching via Linguistic DNA (AI Insights)
        if (genAIInsights != null && existingSpeakers.Count > 0)
        {
            var bestLinguisticMatch = existingSpeakers
                .Select(s => new { Speaker = s, Score = CalculateLinguisticSimilarity(s.Insights, genAIInsights) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (bestLinguisticMatch != null && bestLinguisticMatch.Score > LOCK_THRESHOLD)
            {
                return new SpeakerDecisionResult
                {
                    Action = SpeakerDecisionAction.ConfirmExistingSpeaker,
                    MatchedSpeaker = bestLinguisticMatch.Speaker,
                    Confidence = bestLinguisticMatch.Score,
                    Reasoning = $"High linguistic match ({bestLinguisticMatch.Score}%): same style and role."
                };
            }
        }

        // 3. Fallback to physical match if confidence is decent
        if (utterance.SpeakerConfidence > MATCH_THRESHOLD && !string.IsNullOrEmpty(utterance.ProvisionalSpeakerId))
        {
            var matched = existingSpeakers.FirstOrDefault(s => s.SpeakerId == utterance.ProvisionalSpeakerId);
            if (matched != null)
            {
                return new SpeakerDecisionResult
                {
                    Action = SpeakerDecisionAction.UpdateExistingSpeaker,
                    MatchedSpeaker = matched,
                    Confidence = utterance.SpeakerConfidence,
                    Reasoning = "Moderate physical audio match."
                };
            }
        }

        // 4. Create new if no match found
        return CreateNewSpeakerDecision(utterance, genAIInsights);
    }

    private float CalculateLinguisticSimilarity(SpeakerInsights existing, SpeakerInsights incoming)
    {
        float score = 0;
        if (existing.CommunicationStyle == incoming.CommunicationStyle) score += 40;
        if (existing.AssignedRole == incoming.AssignedRole) score += 30;
        if (existing.SentenceComplexity == incoming.SentenceComplexity) score += 20;
        
        // Match phrases
        var commonPhrases = existing.TypicalPhrases.Intersect(incoming.TypicalPhrases).Count();
        if (commonPhrases > 0) score += 10;

        return Math.Min(score, 100);
    }

    private SpeakerDecisionResult CreateNewSpeakerDecision(UtteranceWithContext utterance, SpeakerInsights? insights)
    {
        var newSpeaker = new SpeakerProfile
        {
            SpeakerId = Guid.NewGuid().ToString("N")[..8],
            DisplayName = insights?.SuggestedName ?? $"Speaker {Random.Shared.Next(1, 99)}",
            Gender = insights?.DetectedGender ?? SpeakerGender.Unknown,
            VoiceFingerprint = utterance.AudioFingerprint,
            Confidence = utterance.SpeakerConfidence
        };

        if (insights != null) newSpeaker.UpdateInsights(insights);

        return new SpeakerDecisionResult
        {
            Action = SpeakerDecisionAction.CreateNewSpeaker,
            NewSpeaker = newSpeaker,
            Confidence = utterance.SpeakerConfidence,
            Reasoning = "No existing speaker matched the physical or linguistic DNA."
        };
    }
}

