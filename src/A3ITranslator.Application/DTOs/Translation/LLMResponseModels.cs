using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Structured fact extraction data from LLM response
/// </summary>
public class FactExtractionData
{
    public bool RequiresFactExtraction { get; set; }
    public List<ExtractedFact> Facts { get; set; } = new();
    public string? Context { get; set; }
    public float Confidence { get; set; }
}

/// <summary>
/// Individual extracted fact from LLM
/// </summary>
public class ExtractedFact
{
    public string Text { get; set; } = string.Empty;
    public string EnglishTranslation { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsVerified { get; set; }
    public string? Context { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Entities { get; set; } = new();
}

/// <summary>
/// Speaker analysis data from LLM response
/// </summary>
public class SpeakerAnalysisData
{
    public string SpeakerGender { get; set; } = "unknown";
    public bool GenderMismatch { get; set; }
    public string? DetectedName { get; set; }
    public bool NameDetected { get; set; }
    public float Confidence { get; set; }
    public string? Reasoning { get; set; }
    public string? GenderSource { get; set; } // "linguistic", "pronoun", or "unknown"
}


