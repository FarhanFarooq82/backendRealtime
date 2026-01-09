using A3ITranslator.Application.Models.SpeakerProfiles;

namespace A3ITranslator.Application.DTOs.Speaker;

/// <summary>
/// Enhanced speaker information with detailed confidence metrics and multi-language support
/// </summary>
public class EnhancedSpeakerInfo
{
    /// <summary>
    /// Unique speaker identifier
    /// </summary>
    public string SpeakerId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable speaker name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Speaker identification confidence (0-100%)
    /// </summary>
    public float IdentificationConfidence { get; set; }

    /// <summary>
    /// Whether this speaker was newly created during this utterance
    /// </summary>
    public bool IsNewSpeaker { get; set; }

    /// <summary>
    /// Whether this speaker identification required user confirmation
    /// </summary>
    public bool RequiredConfirmation { get; set; }

    /// <summary>
    /// Gender detected by GenAI analysis
    /// </summary>
    public SpeakerGender Gender { get; set; } = SpeakerGender.Unknown;

    /// <summary>
    /// Multi-language percentages for this speaker
    /// </summary>
    public Dictionary<string, float> LanguagePercentages { get; set; } = new();

    /// <summary>
    /// Primary language for this speaker
    /// </summary>
    public string PrimaryLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Total utterances from this speaker
    /// </summary>
    public int TotalUtterances { get; set; }

    /// <summary>
    /// When this speaker was first seen
    /// </summary>
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this speaker was last active
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// GenAI insights about this speaker
    /// </summary>
    public SpeakerInsights? Insights { get; set; }

    /// <summary>
    /// Convert from domain SpeakerProfile to DTO
    /// </summary>
    public static EnhancedSpeakerInfo FromDomainModel(SpeakerProfile profile)
    {
        return new EnhancedSpeakerInfo
        {
            SpeakerId = profile.SpeakerId,
            DisplayName = profile.DisplayName,
            IdentificationConfidence = profile.Confidence, // Use existing Confidence property
            IsNewSpeaker = false, // Set by calling context
            RequiredConfirmation = false, // Set by calling context
            Gender = profile.Insights?.DetectedGender ?? SpeakerGender.Unknown,
            LanguagePercentages = profile.Languages.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value.UsagePercentage),
            PrimaryLanguage = profile.GetDominantLanguage() ?? "Unknown",
            TotalUtterances = profile.TotalUtterances,
            FirstSeen = profile.CreatedAt,
            LastSeen = profile.LastActive,
            Insights = profile.Insights
        };
    }
}

/// <summary>
/// Speaker list update notification for frontend
/// </summary>
public class SpeakerListUpdate
{
    /// <summary>
    /// Whether any changes occurred to the speaker list
    /// </summary>
    public bool HasChanges { get; set; }

    /// <summary>
    /// Updated list of all speakers in the session
    /// </summary>
    public List<EnhancedSpeakerInfo> Speakers { get; set; } = new();

    /// <summary>
    /// ID of the speaker who just spoke (if any)
    /// </summary>
    public string? ActiveSpeakerId { get; set; }

    /// <summary>
    /// Type of change that occurred
    /// </summary>
    public SpeakerChangeType ChangeType { get; set; } = SpeakerChangeType.NoChange;

    /// <summary>
    /// Timestamp of this update
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Types of speaker list changes
/// </summary>
public enum SpeakerChangeType
{
    NoChange,
    NewSpeaker,
    ConfirmedSpeaker,
    UpdatedProfile,
    SpeakerSwitch
}
