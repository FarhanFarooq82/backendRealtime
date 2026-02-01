namespace A3ITranslator.Application.Models.Speaker;

/// <summary>
/// Unified Speaker Profile - The single source of truth for "Flow C".
/// Combines Acoustic DNA (Pitch/MFCC) and Linguistic DNA (GenAI insights).
/// </summary>
public class SpeakerProfile
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown Speaker";
    public SpeakerGender Gender { get; set; } = SpeakerGender.Unknown;
    public float Confidence { get; set; } = 0f; 
    public int TotalUtterances { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    
    // Physical DNA (Acoustic characteristics for identification)
    public AudioFingerprint VoiceFingerprint { get; set; } = new();
    
    // Linguistic DNA (GenAI-derived insights)
    public SpeakerInsights Insights { get; set; } = new();

    // Language capabilities
    public Dictionary<string, LanguageCapability> Languages { get; set; } = new();
    public string? PreferredLanguage { get; set; }

    // Control flags
    public bool IsLocked { get; set; } 

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

    public void UpdateAcousticFeatures(float[] embedding)
    {
        // 90/10 Weighted Update for Stability (Centroid Sync)
        const float HISTORY_WEIGHT = 0.9f;
        const float NEW_WEIGHT = 0.1f;

        if (VoiceFingerprint.Embedding.Length == embedding.Length)
        {
             for(int i=0; i<embedding.Length; i++)
             {
                 VoiceFingerprint.Embedding[i] = (VoiceFingerprint.Embedding[i] * HISTORY_WEIGHT) + (embedding[i] * NEW_WEIGHT);
             }
        }
        else if (VoiceFingerprint.Embedding.Length == 0)
        {
            VoiceFingerprint.Embedding = embedding;
        }
    }

    private void RecalculateLanguagePercentages()
    {
        if (TotalUtterances == 0) return;
        foreach (var langCapability in Languages.Values)
        {
            langCapability.UsagePercentage = (float)langCapability.UtteranceCount / TotalUtterances * 100f;
        }
    }

    public string? GetDominantLanguage()
    {
        return PreferredLanguage ?? 
               Languages.OrderByDescending(l => l.Value.UsagePercentage).FirstOrDefault().Key;
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

public class AudioFingerprint
{
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public float MatchConfidence { get; set; } = 0f;
    public string ModelVersion { get; set; } = "v1-onnx";
}

public class SpeakerInsights
{
    public string? SuggestedName { get; set; }
    public SpeakerGender DetectedGender { get; set; } = SpeakerGender.Unknown;
    public string? CommunicationStyle { get; set; } 
    public List<string> TypicalPhrases { get; set; } = new();
    public string? AssignedRole { get; set; } 
    public string? TurnContext { get; set; }
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

