namespace A3ITranslator.Application.DTOs.Speaker;

public class SpeakerInfo
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? KnownLanguage { get; set; }
    public float Confidence { get; set; }
    public bool IsNewSpeaker { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}