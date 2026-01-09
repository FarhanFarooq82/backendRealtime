using A3ITranslator.Application.Models;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Models.SpeakerProfiles;

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
    Task<FactExtractionResult> ProcessFactExtractionAsync(
        FactExtractionData factExtractionData,
        string sessionId,
        string speakerId,
        string speakerName,
        string sourceLanguage,
        int messageSequence);
    
    /// <summary>
    /// Update speaker information based on gender detection
    /// </summary>
    Task<SpeakerProfile?> UpdateSpeakerFromFactsAsync(
        string sessionId,
        string speakerId,
        bool genderMismatch,
        string detectedGender);
    
    /// <summary>
    /// Get all speakers in a session with latest information
    /// </summary>
    Task<List<SpeakerProfile>> GetSessionSpeakersAsync(string sessionId);
    
    /// <summary>
    /// Build fact context for LLM prompts
    /// </summary>
    Task<string> BuildFactContextAsync(string sessionId, int maxLength = 2000);
}

/// <summary>
/// Result of fact extraction processing
/// </summary>
public class FactExtractionResult
{
    public bool Success { get; set; }
    public int FactCount { get; set; }
    public List<SessionFact> ExtractedFacts { get; set; } = new();
    public SpeakerProfile? UpdatedSpeaker { get; set; }
    public bool SpeakerUpdated { get; set; }
    public long ProcessingTimeMs { get; set; }
    public List<string> Messages { get; set; } = new();
}
