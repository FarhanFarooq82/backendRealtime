using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.DTOs.Frontend;

/// <summary>
/// Simplified speaker information for frontend display
/// Contains only essential speaker data with language usage percentages
/// </summary>
public class FrontendSpeakerInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public string Gender { get; set; } = "N/A";
    public int Number { get; set; }
    public Dictionary<string, float> LanguageConfidences { get; set; } = new();

    public static FrontendSpeakerInfo FromDomainModel(SpeakerProfile speaker, int index)
    {
        var dominantLang = speaker.PreferredLanguage ?? 
                           speaker.Languages.OrderByDescending(l => l.Value.UsagePercentage).FirstOrDefault().Key ?? "en";

        var number = index + 1;
        var name = (string.IsNullOrEmpty(speaker.DisplayName) || speaker.DisplayName == "Unknown Speaker") 
                   ? $"Speaker {number}" 
                   : speaker.DisplayName;

        return new FrontendSpeakerInfo
        {
            Id = speaker.SpeakerId,
            Name = name,
            Lang = FrontendConversationItem.GetLanguageName(dominantLang),
            Gender = speaker.Gender == SpeakerGender.Unknown ? "N/A" : speaker.Gender.ToString(),
            Number = number,
            LanguageConfidences = speaker.Languages.ToDictionary(
                l => FrontendConversationItem.GetLanguageName(l.Key), 
                l => l.Value.UsagePercentage)
        };
    }
}

/// <summary>
/// Speaker list update for frontend
/// Sent after speaker identification and update steps finish
/// </summary>
public class FrontendSpeakerListUpdate
{
    /// <summary>
    /// List of speakers with language usage percentages
    /// </summary>
    public List<FrontendSpeakerInfo> Speakers { get; set; } = new();

    /// <summary>
    /// Whether any changes occurred to the speaker list
    /// </summary>
    public bool HasChanges { get; set; }

    /// <summary>
    /// Timestamp of this update
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
