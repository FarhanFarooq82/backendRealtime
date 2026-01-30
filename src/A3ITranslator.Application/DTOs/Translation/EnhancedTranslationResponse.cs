using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Enhanced translation response that matches the exact JSON structure from our Neural Roster prompt.
/// </summary>
public class EnhancedTranslationResponse
{
    // Core translation data - goes to frontend
    public string ImprovedTranscription { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    public string Intent { get; set; } = "SIMPLE_TRANSLATION"; 
    public string TranslationLanguage { get; set; } = string.Empty;
    public string AudioLanguage { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0f;

    // ✨ NEW: Turn Analysis (Speaker ID and Actions)
    public TurnAnalysisData TurnAnalysis { get; set; } = new();

    // ✨ NEW: Session Roster (Full list of participants)
    public List<RosterSpeakerProfile> SessionRoster { get; set; } = new();

    // AI assistance data
    public AIAssistanceData AIAssistance { get; set; } = new();

    // Fact extraction data
    public FactExtractionPayload FactExtraction { get; set; } = new();

    // Processing metadata
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string ProviderUsed { get; set; } = string.Empty;
    public GenAIUsage Usage { get; set; } = new();
}

public class TurnAnalysisData
{
    public string ActiveSpeakerId { get; set; } = string.Empty;
    public float IdentificationConfidence { get; set; } = 0f;
    public string DecisionType { get; set; } = "CONFIRMED"; // "CONFIRMED" | "NEW" | "MERGE"
    public MergeDetails? MergeDetails { get; set; }
}

public class MergeDetails
{
    public string GhostIdToRemove { get; set; } = string.Empty;
    public string TargetIdToKeep { get; set; } = string.Empty;
}

/// <summary>
/// Simplified speaker profile for the roster returned by GenAI
/// </summary>
public class RosterSpeakerProfile
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SocialRole { get; set; } = string.Empty;
    public string EstimatedGender { get; set; } = "Unknown";
    public string PreferredLanguage { get; set; } = string.Empty;
    public string Tone { get; set; } = "casual";
    public bool IsLocked { get; set; }
}

public class AIAssistanceData
{
    public bool TriggerDetected { get; set; } = false;
    public string? Response { get; set; }
    public string? ResponseTranslated { get; set; }
    public float Confidence { get; set; } = 0f;
}

public class FactExtractionPayload
{
    public bool RequiresFactExtraction { get; set; } = false;
    public List<string> Facts { get; set; } = new();
}

public class SpeakerServicePayload
{
    public string SessionId { get; set; } = string.Empty;
    public TurnAnalysisData TurnAnalysis { get; set; } = new();
    public List<RosterSpeakerProfile> Roster { get; set; } = new();
    public string AudioLanguage { get; set; } = string.Empty;
    public float TranscriptionConfidence { get; set; } = 0f;
    public AudioFingerprint? AudioFingerprint { get; set; }
}
