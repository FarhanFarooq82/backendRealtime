using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Concurrent;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// ONNX-based Audio Feature Extractor for Speaker Identification.
/// Uses a pre-trained neural model (ECAPA-TDNN) to extract 128D/256D embeddings.
/// Responsibility: Buffer audio and perform local AI inference.
/// </summary>
public class OnnxAudioFeatureExtractor : IAudioFeatureExtractor, IDisposable
{
    private readonly ILogger<OnnxAudioFeatureExtractor> _logger;
    private readonly InferenceSession _onnxSession;
    private readonly ConcurrentDictionary<string, MemoryStream> _audioBuffers = new();
    private bool _disposed = false;

    public OnnxAudioFeatureExtractor(ILogger<OnnxAudioFeatureExtractor> logger, string modelPath)
    {
        _logger = logger;
        try
        {
            _onnxSession = new InferenceSession(modelPath);
            _logger.LogInformation("✅ ONNX Speaker Model loaded from {Path}", modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load ONNX Speaker Model from {Path}", modelPath);
            throw;
        }
    }

    public Task AccumulateAudioAsync(string connectionId, byte[] audioChunk)
    {
        var buffer = _audioBuffers.GetOrAdd(connectionId, _ => new MemoryStream());
        lock (buffer)
        {
            buffer.Write(audioChunk, 0, audioChunk.Length);
        }
        return Task.CompletedTask;
    }

    public async Task<float[]> ExtractEmbeddingAsync(string connectionId)
    {
        if (!_audioBuffers.TryGetValue(connectionId, out var buffer))
        {
            _logger.LogWarning("⚠️ No audio buffer found for {ConnectionId}", connectionId);
            return Array.Empty<float>();
        }

        byte[] audioData;
        lock (buffer)
        {
            audioData = buffer.ToArray();
        }

        if (audioData.Length < 16000) // Minimum 1 second of audio at 16kHz
        {
            return Array.Empty<float>();
        }

        return await Task.Run(() => PerformInference(audioData));
    }

    private float[] PerformInference(byte[] audioData)
    {
        try
        {
            // 1. Pre-process: Convert PCM bytes to float array (Normalized [-1, 1])
            float[] floatAudio = ConvertPcmToFloat(audioData);

            // 2. Prepare ONNX Input (Batch Size 1, Length N)
            // Use the first input name from metadata automatically
            var inputName = _onnxSession.InputMetadata.Keys.First();
            var inputTensor = new DenseTensor<float>(floatAudio, new[] { 1, floatAudio.Length });
            
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            // 3. Run Inference
            using var results = _onnxSession.Run(inputs);
            var output = results.First().AsEnumerable<float>().ToArray();

            // 4. Normalize the Embedding Vector (L2 Norm) for Cosine Similarity
            return NormalizeEmbedding(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ONNX Inference Error");
            return Array.Empty<float>();
        }
    }

    private float[] ConvertPcmToFloat(byte[] pcmBytes)
    {
        // Assuming 16-bit PCM Mono
        float[] floatArray = new float[pcmBytes.Length / 2];
        for (int i = 0; i < floatArray.Length; i++)
        {
            short sample = BitConverter.ToInt16(pcmBytes, i * 2);
            floatArray[i] = sample / 32768f;
        }
        return floatArray;
    }

    private float[] NormalizeEmbedding(float[] vector)
    {
        double sumSq = vector.Sum(v => (double)v * v);
        float norm = (float)Math.Sqrt(sumSq);
        return vector.Select(v => v / norm).ToArray();
    }

    public void ClearBuffer(string connectionId)
    {
        if (_audioBuffers.TryRemove(connectionId, out var buffer))
        {
            buffer.Dispose();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _onnxSession?.Dispose();
            foreach (var buffer in _audioBuffers.Values)
            {
                buffer.Dispose();
            }
            _disposed = true;
        }
    }
}
