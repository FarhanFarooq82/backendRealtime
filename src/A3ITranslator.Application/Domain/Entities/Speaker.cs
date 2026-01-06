using A3ITranslator.Application.Enums;
using A3ITranslator.Application.Domain.ValueObjects;

namespace A3ITranslator.Application.Domain.Entities;

/// <summary>
/// Core speaker information for session-based speaker identification
/// Combines pitch analysis + transcription analysis results
/// </summary>
public class Speaker
{
    public string SpeakerId { get; private set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; private set; } = string.Empty;
    public int SpeakerNumber { get; private set; }
    public string Language { get; set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public VoiceCharacteristics VoiceCharacteristics { get; set; } = new();
    public SpeakingPatterns SpeakingPatterns { get; set; } = new();
    public DateTime FirstHeard { get; private set; } = DateTime.UtcNow;
    public DateTime LastHeard { get; private set; } = DateTime.UtcNow;
    public int TotalUtterances { get; private set; } = 1;
    public float Confidence { get; set; }
    
    // NEW: Name confirmation properties
    public bool IsNameConfirmed { get; private set; }
    public float NameConfidenceScore { get; private set; }
    public string? ProposedNameChange { get; private set; }
    public int? NameChangeCount { get; private set; }

    // Constructor for EF Core / Persistence
    private Speaker() { }

    public static Speaker Create(string sessionId, int speakerNumber, string displayName = "")
    {
        return new Speaker
        {
            SessionId = sessionId,
            SpeakerNumber = speakerNumber,
            DisplayName = string.IsNullOrEmpty(displayName) ? $"Speaker {speakerNumber}" : displayName,
            SpeakerId = Guid.NewGuid().ToString(),
            FirstHeard = DateTime.UtcNow,
            LastHeard = DateTime.UtcNow
        };
    }

    public void UpdateActivity()
    {
        LastHeard = DateTime.UtcNow;
        TotalUtterances++;
    }

    public void ConfirmName(string name, float confidence)
    {
        DisplayName = name;
        IsNameConfirmed = true;
        NameConfidenceScore = confidence;
        ProposedNameChange = null;
    }
}
