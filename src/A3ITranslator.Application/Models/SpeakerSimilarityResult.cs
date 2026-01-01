/// <summary>
/// Complete result of speaker similarity calculation
/// Provides breakdown of all components for transparency and debugging
/// </summary>
public class SpeakerSimilarityResult
{
    /// <summary>
    /// Overall similarity score (0-1)
    /// Formula: (voice * 0.4) + (vocabulary * 0.4) + (language * 0.2)
    /// Score > 0.90 = Definitely same speaker
    /// Score 0.75-0.90 = Likely same speaker
    /// Score 0.60-0.75 = Uncertain - requires manual review
    /// Score < 0.60 = Different speakers
    /// </summary>
    public float OverallScore { get; set; }

    /// <summary>
    /// Voice characteristics similarity (0-1)
    /// Based on: Fundamental Frequency (30%), Pitch Variance (20%), Spectral Features (50%)
    /// Measures if voice acoustically matches
    /// </summary>
    public float VoiceSimilarity { get; set; }

    /// <summary>
    /// Vocabulary fingerprint similarity (0-1)
    /// Based on: Word usage patterns, unique words, linguistic style
    /// Measures if speaker uses same vocabulary patterns
    /// </summary>
    public float VocabularySimilarity { get; set; }

    /// <summary>
    /// Language match (0 or 1, binary)
    /// 1 = same language, 0 = different language
    /// Language mismatch is veto condition - automatic different speaker
    /// </summary>
    public float LanguageMatch { get; set; }

    /// <summary>
    /// Detailed breakdown of all similarity components
    /// Keys: "voice_characteristics", "vocabulary_fingerprint", "language_match", "overall_score"
    /// Useful for debugging and understanding why scores are high/low
    /// </summary>
    public Dictionary<string, float> BreakdownDetails { get; set; } = new();

    /// <summary>
    /// Human-readable reason for the similarity score
    /// Example: "Same speaker (voice: 92%, vocab: 88%)"
    /// </summary>
    public string Reason { get; set; } = "";
}

