namespace A3ITranslator.Application.DTOs.Audio;

public class PitchAnalysisResult
{
    public bool IsSuccess { get; set; }
    public float FundamentalFrequency { get; set; }
    public float PitchVariance { get; set; }
    public string EstimatedGender { get; set; } = "UNKNOWN";
    public string EstimatedAge { get; set; } = "adult";
    public string VoiceQuality { get; set; } = "unknown";
    public float AnalysisConfidence { get; set; }
    public float AnalyzedDuration { get; set; }
    public float SilenceSkipped { get; set; }
    public float GenderConfidence { get; set; }
    public float AgeConfidence { get; set; }
}
