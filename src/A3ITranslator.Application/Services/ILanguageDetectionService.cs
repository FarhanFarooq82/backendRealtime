using A3ITranslator.Application.Models;
using DomainSession = A3ITranslator.Application.Domain.Entities.ConversationSession;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Speaker-based language detection service interface
/// Pure Domain Architecture
/// </summary>
public interface ILanguageDetectionService
{
    /// <summary>
    /// Get or detect language for a session based on speaker history
    /// </summary>
    Task<LanguageDetectionResult> GetOrDetectLanguageAsync(
        string sessionId, 
        string[] candidateLanguages, 
        DomainSession session);

    /// <summary>
    /// Get known language for a specific speaker
    /// </summary>
    Task<string?> GetSpeakerLanguageAsync(string speakerId, DomainSession session);

    /// <summary>
    /// Update the known language for a speaker
    /// </summary>
    Task UpdateSpeakerLanguageAsync(string speakerId, string language, DomainSession session);

    /// <summary>
    /// Process language detection votes and determine winner
    /// </summary>
    Task<string?> ProcessLanguageVotesAsync(Dictionary<string, int> votes, int threshold = 5);
}
