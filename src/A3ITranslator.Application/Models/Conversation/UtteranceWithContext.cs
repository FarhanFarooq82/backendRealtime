using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Models.Conversation;

/// <summary>
/// Enhanced utterance with language resolution and speaker context
/// </summary>
public class UtteranceWithContext
{
    public string Text { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string DominantLanguage { get; set; } = string.Empty;
    public float TranscriptionConfidence { get; set; } = 0f;
    public string? ProvisionalSpeakerId { get; set; }
    public float SpeakerConfidence { get; set; } = 0f;
    public List<TranscriptionResult> DetectionResults { get; set; } = new();
    public AudioFingerprint? AudioFingerprint { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
