namespace A3ITranslator.Application.Models;

/// <summary>
/// Single conversation turn with speaker and content
/// </summary>
public class ConversationTurn
{
    public string TurnId { get; init; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SpeakerId { get; init; } = string.Empty;
    public string SpeakerName { get; init; } = "Unknown";
    public string Language { get; init; } = "en";
    public string OriginalText { get; init; } = string.Empty;
    public string? TranslatedText { get; set; }
    public string? TargetLanguage { get; set; }
    public TurnType Type { get; init; } = TurnType.Speech;
    public float Confidence { get; init; } = 0.0f;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public Dictionary<string, object> Metadata { get; } = new();
    
    public bool IsFinal { get; set; } = true;
    public bool IsTranslated => !string.IsNullOrEmpty(TranslatedText);
}

public enum TurnType
{
    Speech,
    Translation,
    SystemMessage,
    Error
}