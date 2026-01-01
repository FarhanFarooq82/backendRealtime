namespace A3ITranslator.Application.DTOs.Language;

/// <summary>
/// Language detection result DTO
/// </summary>
public class LanguageDetectionResult
{
    public string Text { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Provider { get; set; } = string.Empty;
}
