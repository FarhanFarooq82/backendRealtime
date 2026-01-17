using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// Implementation of Audio Feature Extraction.
/// currently uses simulated DSP logic for demonstration.
/// </summary>
public class AudioFeatureExtractor : IAudioFeatureExtractor
{
    private readonly ILogger<AudioFeatureExtractor> _logger;
    private readonly Random _random = new Random();

    public AudioFeatureExtractor(ILogger<AudioFeatureExtractor> logger)
    {
        _logger = logger;
    }

    public async Task AccumulateFeaturesAsync(byte[] audioChunk, AudioFingerprint accumulator)
    {
        // 1. Simulate DSP processing time (negligible)
        await Task.Yield();

        // 2. Extract "Live" Features from this chunk
        // In a real implementation: FFT -> Mel Filterbank -> DCT -> MFCC
        // SIMULATION: Generate semi-stable numbers based on audio data to mimic voice consistency.
        
        float chunkPitch = CalculateSimulatedPitch(audioChunk);
        float[] chunkMfcc = CalculateSimulatedMFCC(audioChunk);

        // 3. Update the Accumulator (Rolling Average)
        // Since AudioFingerprint doesn't track count, we use a weighted moving average.
        // We assume the accumulator starts empty (Pitch=0).
        
        if (accumulator.AveragePitch == 0)
        {
            // First chunk
            accumulator.AveragePitch = chunkPitch;
            accumulator.MfccVector = chunkMfcc;
        }
        else
        {
            // Rolling update: 95% history, 5% new chunk (smoothing)
            // This stabilizes the fingerprint over the duration of the utterance.
            accumulator.AveragePitch = (accumulator.AveragePitch * 0.95f) + (chunkPitch * 0.05f);
            
            if (accumulator.MfccVector.Length == chunkMfcc.Length)
            {
                for (int i = 0; i < accumulator.MfccVector.Length; i++)
                {
                    accumulator.MfccVector[i] = (accumulator.MfccVector[i] * 0.95f) + (chunkMfcc[i] * 0.05f);
                }
            }
        }
    }

    public AudioFingerprint FinalizeFingerprint(AudioFingerprint accumulator)
    {
        // In this architecture, the accumulator IS the result.
        // We might simply clamp values or perform final normalization here.
        return accumulator;
    }

    private float CalculateSimulatedPitch(byte[] data)
    {
        // Deterministic simulation based on data sum to keep it consistent for the same audio
        long sum = 0;
        for (int i = 0; i < data.Length; i+=10) sum += data[i];
        
        // Base pitch range 100-250Hz
        return 100f + (sum % 150); 
    }

    private float[] CalculateSimulatedMFCC(byte[] data)
    {
        // Deterministic vector generation
        var vector = new float[13];
        long sum = 0;
        for (int i = 0; i < data.Length; i += 20) sum += data[i];
        
        var rand = new Random((int)sum); // Seed with data content
        for (int i = 0; i < 13; i++)
        {
            vector[i] = (float)rand.NextDouble();
        }
        return vector;
    }
}
