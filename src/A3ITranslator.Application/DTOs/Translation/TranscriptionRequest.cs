using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Request for the complete transcription processing pipeline
/// </summary>
public class TranscriptionRequest
{
    public string Transcription { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public SpeakerProfile? SpeakerInfo { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
