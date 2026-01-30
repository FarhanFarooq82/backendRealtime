using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Interface for raw audio feature extraction (DSP Logic).
/// Responsible for calculating Pitch and MFCC features from audio data.
/// </summary>
public interface IAudioFeatureExtractor
{
    /// <summary>
    /// Accumulate audio chunks for later embedding extraction.
    /// </summary>
    Task AccumulateAudioAsync(string connectionId, byte[] audioChunk);

    /// <summary>
    /// Extract a neural embedding (Voice DNA) from the accumulated audio.
    /// </summary>
    Task<float[]> ExtractEmbeddingAsync(string connectionId);

    /// <summary>
    /// Clear the audio buffer for a specific connection.
    /// </summary>
    void ClearBuffer(string connectionId);
}
