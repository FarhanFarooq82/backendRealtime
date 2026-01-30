using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

public interface ISpeakerIdentificationService
{
    /// <summary>
    /// Compare a live fingerprint against a list of candidate profiles.
    /// Returns a detailed scorecard for GenAI analysis.
    /// </summary>
    List<SpeakerComparisonResult> CompareFingerprints(AudioFingerprint probe, List<SpeakerProfile> candidates);
}

public class SpeakerComparisonResult
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public float SimilarityScore { get; set; } // Cosine Similarity
    
    public override string ToString()
    {
        return $"[Speaker: {DisplayName}, Similarity: {SimilarityScore:P0}]";
    }
}