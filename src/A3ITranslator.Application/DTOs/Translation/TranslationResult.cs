namespace A3ITranslator.Application.DTOs.Translation;

using A3ITranslator.Application.Models;

/// <summary>
/// Result from translation processing containing all relevant data
/// </summary>
public class TranslationResult
{
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public float Confidence { get; set; } = 1.0f;
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public List<SessionFact> Facts { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}
