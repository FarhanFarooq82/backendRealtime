using A3ITranslator.Application.Enums;

namespace A3ITranslator.Application.Models;

/// <summary>
/// Core speaker information for session-based speaker identification
/// Combines pitch analysis + transcription analysis results
/// </summary>
public class Speaker
{
    public string SpeakerId { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = string.Empty;
    public int SpeakerNumber { get; set; }
    public string Language { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public VoiceCharacteristics VoiceCharacteristics { get; set; } = new();
    public SpeakingPatterns SpeakingPatterns { get; set; } = new();
    public DateTime FirstHeard { get; set; } = DateTime.UtcNow;
    public DateTime LastHeard { get; set; } = DateTime.UtcNow;
    public int TotalUtterances { get; set; } = 1;
    public float Confidence { get; set; }
    
    // NEW: Name confirmation properties
    public bool IsNameConfirmed { get; set; }
    public float NameConfidenceScore { get; set; }
    public string? ProposedNameChange { get; set; }
    public int? NameChangeCount { get; set; }
}


public class SpeakingPatterns
{
    public Dictionary<string, float> VocabularyFingerprint { get; set; } = new();
    public float SpeechRateWPM { get; set; }
    public int TypicalUtteranceLength { get; set; }
    public DateTime LastAnalyzedAt { get; set; } = DateTime.UtcNow;
    public float AnalysisConfidence { get; set; }
}

public class SpeakerMatchResult
{
    public bool IsMatch { get; set; }
    public Speaker? Speaker { get; set; }
    public float Confidence { get; set; }
    public float SimilarityScore { get; set; }
    public bool IsNewSpeaker { get; set; }
    public Dictionary<string, float> SimilarityBreakdown { get; set; } = new();
}

public class SessionSpeakerInfo
{
    public string SessionId { get; set; } = string.Empty;
    public List<Speaker> Speakers { get; set; } = new();
    public Dictionary<string, List<Speaker>> SpeakersByLanguage => 
        Speakers.GroupBy(s => s.Language).ToDictionary(g => g.Key, g => g.ToList());
    public int TotalSpeakers => Speakers.Count;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}
