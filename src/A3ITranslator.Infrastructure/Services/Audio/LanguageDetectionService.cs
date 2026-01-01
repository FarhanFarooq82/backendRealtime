using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// Speaker-based language detection service (Fix Issue #1)
/// Single Responsibility: Language detection per speaker
/// </summary>
public class LanguageDetectionService : ILanguageDetectionService
{
    private readonly ILogger<LanguageDetectionService> _logger;
    private readonly ISpeakerIdentificationService _speakerService;

    public LanguageDetectionService(
        ILogger<LanguageDetectionService> logger,
        ISpeakerIdentificationService speakerService)
    {
        _logger = logger;
        _speakerService = speakerService;
    }

    public async Task<LanguageDetectionResult> GetOrDetectLanguageAsync(
        string sessionId, 
        string[] candidateLanguages,
        ConversationSession session)
    {
        try
        {
            // Get current speaker from session
            var currentSpeakerId = session.CurrentSpeakerId;
            
            if (!string.IsNullOrEmpty(currentSpeakerId))
            {
                // Check if this speaker has a known language
                var speakerLanguage = await GetSpeakerLanguageAsync(currentSpeakerId, session);
                
                if (!string.IsNullOrEmpty(speakerLanguage))
                {
                    _logger.LogInformation("✅ Using known language {Language} for speaker {SpeakerId}", 
                        speakerLanguage, currentSpeakerId);
                        
                    session.PrimaryLanguage = speakerLanguage;
                    session.IsLanguageConfirmed = true;
                    
                    return new LanguageDetectionResult
                    {
                        Language = speakerLanguage,
                        IsKnown = true,
                        RequiresDetection = false,
                        CurrentSpeakerId = currentSpeakerId
                    };
                }
            }

            // Language detection needed
            _logger.LogInformation("🔍 Language detection required for session {SessionId}, speaker {SpeakerId}. Candidates: {Languages}", 
                sessionId, currentSpeakerId ?? "unknown", string.Join(", ", candidateLanguages));

            return new LanguageDetectionResult
            {
                Language = candidateLanguages.FirstOrDefault() ?? "en",
                IsKnown = false,
                RequiresDetection = true,
                CandidateLanguages = candidateLanguages,
                CurrentSpeakerId = currentSpeakerId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in language detection for session {SessionId}", sessionId);
            
            return new LanguageDetectionResult
            {
                Language = candidateLanguages.FirstOrDefault() ?? "en",
                IsKnown = false,
                RequiresDetection = true,
                CandidateLanguages = candidateLanguages
            };
        }
    }

    public async Task<string?> GetSpeakerLanguageAsync(string speakerId, ConversationSession session)
    {
        var speaker = session.Speakers.GetSpeaker(speakerId);
        return await Task.FromResult(speaker?.KnownLanguage);
    }

    public async Task UpdateSpeakerLanguageAsync(string speakerId, string language, ConversationSession session)
    {
        var speaker = session.Speakers.GetSpeaker(speakerId);
        if (speaker != null)
        {
            speaker.KnownLanguage = language;
            _logger.LogInformation("💾 Updated language {Language} for speaker {SpeakerId}", 
                language, speakerId);
        }
        
        await Task.CompletedTask;
    }

    public async Task<string?> ProcessLanguageVotesAsync(Dictionary<string, int> votes, int threshold = 5)
    {
        if (votes.Count == 0) return null;
        
        var winner = votes.OrderByDescending(kv => kv.Value).First();
        
        if (winner.Value >= threshold)
        {
            _logger.LogInformation("🎯 Language winner: {Language} with {Votes} votes (threshold: {Threshold})", 
                winner.Key, winner.Value, threshold);
            return winner.Key;
        }

        return await Task.FromResult<string?>(null);
    }
}