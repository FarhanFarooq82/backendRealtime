using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Modern interface for building translation prompts - pure async implementation
/// </summary>
public interface ITranslationPromptService
{
    Task<(string systemPrompt, string userPrompt)> BuildTranslationPromptsAsync(EnhancedTranslationRequest request);
}
