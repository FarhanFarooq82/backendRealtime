namespace A3ITranslator.Application.Models;

/// <summary>
/// Result of language detection process
/// </summary>
public class LanguageDetectionResult
{
    public string Language { get; set; } = string.Empty;
    public bool IsKnown { get; set; }
    public bool RequiresDetection { get; set; }
    public string[] CandidateLanguages { get; set; } = Array.Empty<string>();
    public string? CurrentSpeakerId { get; set; }
}