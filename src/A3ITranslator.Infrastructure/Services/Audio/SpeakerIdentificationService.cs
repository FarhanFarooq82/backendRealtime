using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// Speaker Identification Service (Modern Neural Architecture)
/// Responsibility: Compare neural embeddings using Cosine Similarity.
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
            var targetEmbedding = profile.VoiceFingerprint.Embedding;
            if (targetEmbedding.Length == 0 || probe.Embedding.Length == 0) continue;
            if (targetEmbedding.Length != probe.Embedding.Length) continue;

            // Neural Embeddings are pre-normalized, so Cosine Similarity is just a Dot Product
            float similarity = CalculateDotProduct(probe.Embedding, targetEmbedding);

            // Clamp results to 0-1 range for easier thresholding
            float normalizedScore = (similarity + 1.0f) / 2.0f;

            results.Add(new SpeakerComparisonResult
            {
                SpeakerId = profile.SpeakerId,
                DisplayName = profile.DisplayName,
                SimilarityScore = similarity // Using raw cosine similarity [-1, 1]
            });
        }

        return results.OrderByDescending(r => r.SimilarityScore).ToList();
    }

    private float CalculateDotProduct(float[] vecA, float[] vecB)
    {
        float dot = 0f;
        for (int i = 0; i < vecA.Length; i++)
        {
            dot += vecA[i] * vecB[i];
        }
        return dot;
    }
}