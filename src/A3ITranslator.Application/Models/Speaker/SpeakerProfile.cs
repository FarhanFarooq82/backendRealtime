using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Models.SpeakerProfiles;

/// <summary>
/// Session-scoped speaker profile with multi-language capabilities and "Linguistic DNA"
/// </summary>
public class SpeakerProfile
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown Speaker";
    public SpeakerGender Gender { get; set; } = SpeakerGender.Unknown;
    public float Confidence { get; set; } = 0f; // 0-100%
    public int TotalUtterances { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    
    // Language capabilities with utterance-based percentages
    public Dictionary<string, LanguageCapability> Languages { get; set; } = new();
    
    // Audio characteristics (The "Fingerprint" - Physical DNA)
    public AudioFingerprint? VoiceFingerprint { get; set; }
    
    // GenAI-derived insights (The "Personality" - Linguistic DNA)
    public SpeakerInsights Insights { get; set; } = new();

    // Context tracking
    public bool IsLocked { get; set; } // When we are 80%+ sure, we lock to avoid flickering
    public string? LastRole { get; set; }
    
    /// <summary>
    /// Add utterance and update language statistics
    /// </summary>
    public void AddUtterance(string language, float transcriptionConfidence)
    {
        TotalUtterances++;
        LastActive = DateTime.UtcNow;
        
        if (!Languages.ContainsKey(language))
        {
            Languages[language] = new LanguageCapability 
            { 
                Language = language,
                UtteranceCount = 0,
                AverageConfidence = 0f
            };
        }
        
        var langCapability = Languages[language];
        langCapability.UtteranceCount++;
        langCapability.LastUsed = DateTime.UtcNow;
        
        langCapability.AverageConfidence = 
            ((langCapability.AverageConfidence * (langCapability.UtteranceCount - 1)) + transcriptionConfidence) 
            / langCapability.UtteranceCount;
        
        RecalculateLanguagePercentages();
    }
    
    public void UpdateInsights(SpeakerInsights insights)
    {
        Insights = insights;
        
        if (!string.IsNullOrEmpty(insights.SuggestedName) && 
            (DisplayName == "Unknown Speaker" || string.IsNullOrEmpty(DisplayName)))
        {
            DisplayName = insights.SuggestedName;
        }
        
        if (insights.DetectedGender != SpeakerGender.Unknown)
        {
            Gender = insights.DetectedGender;
        }

        if (insights.AnalysisConfidence > 80f)
        {
            IsLocked = true;
        }
    }
    
    public void UpdateLanguageUsage(string language, float confidence)
    {
        AddUtterance(language, confidence);
    }
    
    private void RecalculateLanguagePercentages()
    {
        if (TotalUtterances == 0) return;
        
        foreach (var langCapability in Languages.Values)
        {
            langCapability.UsagePercentage = 
                (float)langCapability.UtteranceCount / TotalUtterances * 100f;
        }
    }
    
    public string? GetDominantLanguage()
    {
        return Languages.Values
            .OrderByDescending(l => l.UsagePercentage)
            .FirstOrDefault()?.Language;
    }
}

public class LanguageCapability
{
    public string Language { get; set; } = string.Empty;
    public int UtteranceCount { get; set; } = 0;
    public float UsagePercentage { get; set; } = 0f;
    public float AverageConfidence { get; set; } = 0f;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Physical Audio DNA (MFCC/Pitch characteristics)
/// </summary>
public class AudioFingerprint
{
    public float AveragePitch { get; set; }
    public float SpeechRate { get; set; }
    public float[] SpectralCentroid { get; set; } = Array.Empty<float>();
    public string CharacteristicHash { get; set; } = string.Empty;
    public float MatchConfidence { get; set; } = 0f;
}

/// <summary>
/// Personality/Linguistic DNA derived from transcription
/// </summary>
public class SpeakerInsights
{
    public string? SuggestedName { get; set; }
    public SpeakerGender DetectedGender { get; set; } = SpeakerGender.Unknown;
    
    // Linguistic DNA
    public string? CommunicationStyle { get; set; } // Formal, Excited, Technical
    public List<string> TypicalPhrases { get; set; } = new();
    public string? AssignedRole { get; set; } // Host, Expert, Listener
    public string? SentenceComplexity { get; set; } // Simple, Sophisticated
    public string? TurnContext { get; set; } // "Answering Sarah", "Initiating Topic"

    public float AnalysisConfidence { get; set; } = 0f;
    public DateTime LastAnalyzed { get; set; } = DateTime.UtcNow;
}

public enum SpeakerGender
{
    Unknown = 0,
    Male = 1,
    Female = 2,
    NonBinary = 3
}

