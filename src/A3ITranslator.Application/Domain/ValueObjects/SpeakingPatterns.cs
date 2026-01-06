namespace A3ITranslator.Application.Domain.ValueObjects;

public class SpeakingPatterns
{
    public Dictionary<string, float> VocabularyFingerprint { get; set; } = new();
    public float SpeechRateWPM { get; set; }
    public int TypicalUtteranceLength { get; set; }
    public DateTime LastAnalyzedAt { get; set; } = DateTime.UtcNow;
    public float AnalysisConfidence { get; set; }
}
