using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Interface for raw audio feature extraction (DSP Logic).
/// Responsible for calculating Pitch and MFCC features from audio data.
/// </summary>
public interface IAudioFeatureExtractor
{
    /// <summary>
    /// Extract features from a single audio chunk and update the rolling accumulator.
    /// This allows for zero-latency Feature Extraction by processing in background.
    /// </summary>
    /// <param name="audioChunk">Raw PCM audio chunk</param>
    /// <param name="accumulator">The rolling fingerprint state to update</param>
    Task AccumulateFeaturesAsync(byte[] audioChunk, AudioFingerprint accumulator);

    /// <summary>
    /// Finalize and normalize the fingerprint after collection is complete.
    /// </summary>
    /// <param name="accumulator">The accumulated state</param>
    /// <returns>A normalized, ready-to-use AudioFingerprint</returns>
    AudioFingerprint FinalizeFingerprint(AudioFingerprint accumulator);
}
