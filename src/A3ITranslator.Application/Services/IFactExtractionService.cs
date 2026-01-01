using A3ITranslator.Application.Models;
using A3ITranslator.Application.DTOs.Translation;

using SpeakerModel = A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service for extracting and managing conversation facts
/// Handles fact extraction from LLM responses and session-level fact management
/// </summary>
public interface IFactExtractionService
{
    /// <summary>
    /// Process fact extraction from LLM translation response
    /// Extracts facts and updates speaker information if needed
    /// </summary>
    /// <param name="factExtractionData">Fact extraction section from LLM response</param>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="speakerId">Speaker identifier</param>
    /// <param name="speakerName">Current speaker display name</param>
    /// <param name="sourceLanguage">Original language of the content</param>
    /// <param name="messageSequence">Message sequence number</param>
    /// <returns>Processing result with extracted facts</returns>
    Task<FactExtractionResult> ProcessFactExtractionAsync(
        FactExtractionData factExtractionData,
        string sessionId,
        string speakerId,
        string speakerName,
        string sourceLanguage,
        int messageSequence);
    
    /// <summary>
    /// Update speaker information based on gender detection
    /// Name updates are handled separately in AudioController
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="speakerId">Speaker identifier</param>
    /// <param name="genderMismatch">Whether gender mismatch was detected</param>
    /// <param name="detectedGender">Gender detected from speech patterns</param>
    /// <returns>Updated speaker information</returns>
    Task<SpeakerModel?> UpdateSpeakerFromFactsAsync(
        string sessionId,
        string speakerId,
        bool genderMismatch,
        string detectedGender);
    
    /// <summary>
    /// Get all speakers in a session with latest information
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <returns>List of session speakers with current facts</returns>
    Task<List<SpeakerModel>> GetSessionSpeakersAsync(string sessionId);
    
    /// <summary>
    /// Build fact context for LLM prompts
    /// Creates formatted context suitable for translation prompts
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="maxLength">Maximum context length in characters</param>
    /// <returns>Formatted fact context string</returns>
    Task<string> BuildFactContextAsync(string sessionId, int maxLength = 2000);
}

/// <summary>
/// Result of fact extraction processing
/// </summary>
public class FactExtractionResult
{
    /// <summary>
    /// Whether processing was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Number of facts extracted
    /// </summary>
    public int FactCount { get; set; }
    
    /// <summary>
    /// List of extracted facts
    /// </summary>
    public List<SessionFact> ExtractedFacts { get; set; } = new();
    
    /// <summary>
    /// Updated speaker information (if changes were made)
    /// </summary>
    public Speaker? UpdatedSpeaker { get; set; }
    
    /// <summary>
    /// Whether speaker information was updated
    /// </summary>
    public bool SpeakerUpdated { get; set; }
    
    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; set; }
    
    /// <summary>
    /// Any errors or warnings during processing
    /// </summary>
    public List<string> Messages { get; set; } = new();
}
