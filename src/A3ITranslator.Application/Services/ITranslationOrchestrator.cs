using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Orchestrates the translation processing with prompt service and GenAI
/// </summary>
public interface ITranslationOrchestrator
{
    /// <summary>
    /// Process translation with conditional AI assistance
    /// </summary>
    Task<TranslationResponse> ProcessTranslationAsync(EnhancedTranslationRequest request);
}
