using A3ITranslator.Application.Domain.Enums;

namespace A3ITranslator.Application.Domain.Entities;

/// <summary>
/// Single conversation turn with speaker and content
/// </summary>
public class ConversationTurn
{
    public string TurnId { get; private set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public string SpeakerId { get; private set; } = string.Empty;
    public string SpeakerName { get; private set; } = "Unknown";
    public string Language { get; private set; } = "en";
    public string OriginalText { get; private set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public string? TargetLanguage { get; set; }
    public TurnType Type { get; private set; } = TurnType.Speech;
    public float Confidence { get; private set; } = 0.0f;
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public Dictionary<string, object> Metadata { get; } = new();
    
    public bool IsFinal { get; set; } = true;
    public bool IsTranslated => !string.IsNullOrEmpty(TranslatedText);

    private ConversationTurn() { }

    public static ConversationTurn CreateSpeech(string speakerId, string speakerName, string text, string language)
    {
        return new ConversationTurn
        {
            SpeakerId = speakerId,
            SpeakerName = speakerName,
            OriginalText = text,
            Language = language,
            Type = TurnType.Speech,
            Timestamp = DateTime.UtcNow
        };
    }

    public ConversationTurn SetMetadata(string key, object value)
    {
        Metadata[key] = value;
        return this;
    }

    public ConversationTurn SetTranslation(string translatedText, string targetLanguage)
    {
        TranslatedText = translatedText;
        TargetLanguage = targetLanguage;
        return this;
    }
}
