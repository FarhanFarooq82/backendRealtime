using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Interface for building translation prompts
/// </summary>
public interface ITranslationPromptService
{
    (string systemPrompt, string userPrompt) BuildTranslationPrompts(EnhancedTranslationRequest request);
}
