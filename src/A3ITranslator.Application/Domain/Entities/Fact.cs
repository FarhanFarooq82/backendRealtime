using System;

namespace A3ITranslator.Application.Domain.Entities;

/// <summary>
/// Represents a structured fact extracted from the conversation
/// </summary>
public class Fact
{
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string SourceSpeakerId { get; private set; } = string.Empty;
    public string SourceSpeakerName { get; private set; } = "Unknown";
    public string TurnId { get; private set; } = string.Empty;
    public int TurnNumber { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; private set; } = DateTime.UtcNow;

    private Fact() { }

    public static Fact Create(
        string key, 
        string value, 
        string speakerId, 
        string speakerName, 
        string turnId,
        int turnNumber,
        DateTime timestamp)
    {
        return new Fact
        {
            Key = key,
            Value = value,
            SourceSpeakerId = speakerId,
            SourceSpeakerName = speakerName,
            TurnId = turnId,
            TurnNumber = turnNumber,
            CreatedAt = timestamp,
            LastUpdatedAt = timestamp
        };
    }

    public void UpdateValue(string newValue, string speakerId, string speakerName, string turnId, int turnNumber, DateTime timestamp)
    {
        Value = newValue;
        SourceSpeakerId = speakerId;
        SourceSpeakerName = speakerName;
        TurnId = turnId;
        TurnNumber = turnNumber;
        LastUpdatedAt = timestamp;
    }
}
