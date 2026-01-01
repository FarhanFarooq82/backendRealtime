namespace A3ITranslator.Application.Models;

/// <summary>
/// Result of speech-to-text transcription
/// </summary>
public class TranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public bool IsFinal { get; set; }
    public double Confidence { get; set; }
    public TimeSpan Timestamp { get; set; } = TimeSpan.Zero;
    public Dictionary<string, object> Metadata { get; } = new();
}