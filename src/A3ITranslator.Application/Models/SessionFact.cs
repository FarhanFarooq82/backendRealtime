namespace A3ITranslator.Application.Models;

public class SessionFact
{
    public string FactId { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public string FactContent { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}
