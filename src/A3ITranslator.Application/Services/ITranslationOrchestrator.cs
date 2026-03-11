using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Orchestrates the translation processing with prompt service and GenAI
/// </summary>
public interface ITranslationOrchestrator
{


    /// <summary>
    /// Generate a native-language summary with AI-generated headings
    /// </summary>
    Task<string> GenerateSummaryInLanguageAsync(string conversationHistory, string language);



    Task<(string systemPrompt, string userPrompt)> BuildAgent2PromptsAsync(EnhancedTranslationRequest request);
    Task<(string systemPrompt, string userPrompt)> BuildAgent3PromptsAsync(EnhancedTranslationRequest request);
    Task<(string systemPrompt, string userPrompt)> BuildFastIntentPromptsAsync(string transcription);
}
