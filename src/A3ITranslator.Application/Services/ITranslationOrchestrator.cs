using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Orchestrates the translation processing with prompt service and GenAI
/// </summary>
public interface ITranslationOrchestrator
{

    /// <summary>
    /// Process enhanced translation with structured response for maximum performance
    /// Returns new structured format that routes different data to different services
    /// </summary>
    Task<EnhancedTranslationResponse> ProcessEnhancedTranslationAsync(EnhancedTranslationRequest request);

    /// <summary>
    /// Generate a native-language summary with AI-generated headings
    /// </summary>
    Task<string> GenerateSummaryInLanguageAsync(string conversationHistory, string language);
}
