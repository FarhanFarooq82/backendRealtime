using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Orchestrates the translation processing with prompt service and GenAI
/// </summary>
public interface ITranslationOrchestrator
{
    /// <summary>
    /// Process translation with conditional AI assistance (Legacy method - for backward compatibility)
    /// </summary>
    Task<TranslationResponse> ProcessTranslationAsync(EnhancedTranslationRequest request);

    /// <summary>
    /// Process enhanced translation with structured response for maximum performance
    /// Returns new structured format that routes different data to different services
    /// </summary>
    Task<EnhancedTranslationResponse> ProcessEnhancedTranslationAsync(EnhancedTranslationRequest request);

    /// <summary>
    /// Generate a summary of the conversation based on history
    /// </summary>
    Task<string> GenerateConversationSummaryAsync(string conversationHistory, string primaryLanguage, string secondaryLanguage);
}
