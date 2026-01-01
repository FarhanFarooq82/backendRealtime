using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service for content filtering and prompt sanitization to avoid AI content policy violations
/// </summary>
public interface IContentFilterService
{
    /// <summary>
    /// Sanitize user text to prevent content filter issues
    /// </summary>
    Task<ContentFilterResult> SanitizeTextAsync(string text, string language);
    
    /// <summary>
    /// Check if the text contains potential content filter triggers
    /// </summary>
    Task<bool> IsContentSafeAsync(string text, string language);
    
    /// <summary>
    /// Clean and prepare prompts for AI processing
    /// </summary>
    Task<string> SanitizePromptAsync(string prompt);
    
    /// <summary>
    /// Handle content filter errors and provide fallback responses
    /// </summary>
    Task<TranslationResponse> HandleContentFilterErrorAsync(EnhancedTranslationRequest request, string errorDetails);
}

/// <summary>
/// Result of content filtering operation
/// </summary>
public class ContentFilterResult
{
    public bool IsFiltered { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string SanitizedText { get; set; } = string.Empty;
    public string FilterReason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> RemovedPatterns { get; set; } = new();
}
