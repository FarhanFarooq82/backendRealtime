namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Result of trigger phrase detection for AI assistance
/// </summary>
public class TriggerDetectionResult
{
    public bool TriggerDetected { get; set; }
    public string? DetectedTrigger { get; set; }
    public string? Language { get; set; }
    public bool NeedsAIConfirmation { get; set; }
    public float Confidence { get; set; }
}
