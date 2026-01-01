using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service interface for detecting trigger phrases that indicate AI assistance requests
/// </summary>
public interface ITriggerDetectionService
{
    /// <summary>
    /// Detect if transcription contains trigger phrases for AI assistance
    /// </summary>
    TriggerDetectionResult DetectTrigger(string transcription, string sourceLanguage, string targetLanguage);
    
    /// <summary>
    /// Add custom trigger phrases for a language
    /// </summary>
    void AddTriggerPhrases(string language, IEnumerable<string> phrases);
    
    /// <summary>
    /// Get all supported languages for trigger detection
    /// </summary>
    IEnumerable<string> GetSupportedLanguages();
}
