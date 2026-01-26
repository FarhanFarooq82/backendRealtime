using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Modern interface for building translation prompts - pure async implementation
/// </summary>
public interface ITranslationPromptService
{
    Task<(string systemPrompt, string userPrompt)> BuildTranslationPromptsAsync(EnhancedTranslationRequest request);

    /// <summary>
    /// Build prompts for generating a concise conversation summary
    /// </summary>
    Task<(string systemPrompt, string userPrompt)> BuildSummaryPromptsAsync(string conversationHistory, string primaryLanguage, string secondaryLanguage);
}
