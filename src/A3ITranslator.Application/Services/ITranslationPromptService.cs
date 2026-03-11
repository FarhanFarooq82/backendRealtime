using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Modern interface for building translation prompts - pure async implementation
/// </summary>
public interface ITranslationPromptService
{


    Task<(string systemPrompt, string userPrompt)> BuildAgent2PromptsAsync(EnhancedTranslationRequest request);
    Task<(string systemPrompt, string userPrompt)> BuildAgent3PromptsAsync(EnhancedTranslationRequest request);
    Task<(string systemPrompt, string userPrompt)> BuildFastIntentPromptsAsync(string transcription);

    /// <summary>
    /// Build prompts for generating a native-language summary with AI-generated headings
    /// </summary>
    Task<(string systemPrompt, string userPrompt)> BuildNativeSummaryPromptsAsync(string conversationHistory, string language);
}
