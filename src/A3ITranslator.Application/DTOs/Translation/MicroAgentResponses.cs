using A3ITranslator.Application.Models.Speaker;
using System.Collections.Generic;

namespace A3ITranslator.Application.DTOs.Translation;



/// <summary>
/// Response from the Speaker Identification Agent (Agent 2).
/// </summary>
public class Agent2Response
{
    public string ImprovedTranscription { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty; // From Super Agent 2
    public string EstimatedGender { get; set; } = "Unknown";
    public float Confidence { get; set; } = 0f;
    public TurnAnalysisData TurnAnalysis { get; set; } = new();
    public List<RosterSpeakerProfile> SessionRoster { get; set; } = new();
    public AIAssistanceData? AIAssistance { get; set; }
}

/// <summary>
/// Response from the Fact Extraction Agent (Agent 3).
/// </summary>
public class Agent3Response
{
    public FactExtractionPayload FactExtraction { get; set; } = new();
}


