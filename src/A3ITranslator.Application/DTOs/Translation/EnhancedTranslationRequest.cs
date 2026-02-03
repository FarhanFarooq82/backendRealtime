using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Request for enhanced translation with conditional AI assistance
/// </summary>
public class EnhancedTranslationRequest
{
    public string Text { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public bool TriggerDetected { get; set; }
    public string? DetectedTrigger { get; set; }
    public SpeakerProfile? SpeakerInfo { get; set; }
    public Dictionary<string, object>? SessionContext { get; set; }
    public string TurnId { get; set; } = string.Empty;
    public bool IsPulse { get; set; }
}
