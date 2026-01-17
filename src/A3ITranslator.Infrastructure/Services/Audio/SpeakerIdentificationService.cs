using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// Speaker Identification Service (Features-Only Architecture)
/// Responsibility: Compare raw "AudioFingerprints" and return a scorecard.
/// Does NOT make decisions; only provides mathematical evidence to GenAI.
/// </summary>
public class SpeakerIdentificationService : ISpeakerIdentificationService
{
    private readonly ILogger<SpeakerIdentificationService> _logger;

    public SpeakerIdentificationService(ILogger<SpeakerIdentificationService> logger)
    {
        _logger = logger;
    }

    public List<SpeakerComparisonResult> CompareFingerprints(AudioFingerprint probe, List<SpeakerProfile> candidates)
    {
        var results = new List<SpeakerComparisonResult>();

        foreach (var profile in candidates)
        {
            var finger = profile.VoiceFingerprint;
            if (finger.MfccVector.Length == 0 && finger.AveragePitch == 0) continue; // Skip empty profiles

            // 1. Pitch Similarity (30% weight)
            // Logic: 40Hz difference is considered a mismatch (0 score)
            float pitchDist = Math.Abs(probe.AveragePitch - finger.AveragePitch);
            float pitchScore = Math.Max(0, 1.0f - (pitchDist / 40.0f));

            // 2. Timbre Similarity (70% weight)
            // Logic: Euclidean distance on normalized MFCC vectors
            float timbreScore = CalculateTimbreSimilarity(probe.MfccVector, finger.MfccVector);

            // 3. Composite Score
            float composite = (pitchScore * 0.3f) + (timbreScore * 0.7f);

            results.Add(new SpeakerComparisonResult
            {
                SpeakerId = profile.SpeakerId,
                DisplayName = profile.DisplayName,
                PitchSimilarity = pitchScore,
                TimbreSimilarity = timbreScore,
                CompositeScore = composite
            });
        }

        return results.OrderByDescending(r => r.CompositeScore).ToList();
    }

    private float CalculateTimbreSimilarity(float[] probeVec, float[] targetVec)
    {
        if (probeVec.Length != targetVec.Length || probeVec.Length == 0) 
            return 0f;

        float sumSq = 0f;
        for (int i = 0; i < probeVec.Length; i++)
        {
            sumSq += (float)Math.Pow(probeVec[i] - targetVec[i], 2);
        }
        
        float dist = (float)Math.Sqrt(sumSq);
        // Assuming vectors are normalized roughly 0-1, max dist approx 1.0-1.5
        // We clamp to 0-1 range
        return Math.Max(0, 1.0f - dist);
    }
}