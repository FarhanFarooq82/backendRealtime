using System.Text;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// DEBUG ONLY: Audio chunk collector and WAV file writer for testing
/// This will be deleted after debugging is complete
/// SINGLETON PATTERN to ensure single instance across application
/// </summary>
public class AudioDebugWriter
{
    private static AudioDebugWriter? _instance;
    private static readonly object _lock = new object();
    private readonly string _outputDirectory;
    private readonly Dictionary<string, List<byte[]>> _sessionAudioBuffers = new();
    private readonly Dictionary<string, int> _sessionChunkCounts = new();

    private AudioDebugWriter()
    {
        _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "A3ITranslator_AudioDebug");
        Directory.CreateDirectory(_outputDirectory);
        Console.WriteLine($"🎵 DEBUG: Audio files will be saved to: {_outputDirectory}");
    }

    public static AudioDebugWriter Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                        _instance = new AudioDebugWriter();
                }
            }
            return _instance;
        }
    }

    public void AddAudioChunk(string connectionId, byte[] audioChunk)
    {
        if (audioChunk == null || audioChunk.Length == 0) return;

        lock (_lock)
        {
            if (!_sessionAudioBuffers.ContainsKey(connectionId))
            {
                _sessionAudioBuffers[connectionId] = new List<byte[]>();
                _sessionChunkCounts[connectionId] = 0;
                Console.WriteLine($"🎵 AUDIO DEBUG: Starting new session for {connectionId}");
            }

            _sessionAudioBuffers[connectionId].Add(audioChunk);
            _sessionChunkCounts[connectionId]++;

            Console.WriteLine($"🎵 AUDIO DEBUG: Captured chunk #{_sessionChunkCounts[connectionId]} ({audioChunk.Length} bytes) for {connectionId}");
            Console.WriteLine($"   📊 First 5 bytes: [{string.Join(", ", audioChunk.Take(5))}]");
            Console.WriteLine($"   📊 Total chunks: {_sessionChunkCounts[connectionId]}, Total bytes: {_sessionAudioBuffers[connectionId].Sum(c => c.Length)}");

            // 🔥 DEBUG: Auto-commit every 50 chunks to create smaller test files
            if (_sessionChunkCounts[connectionId] % 50 == 0)
            {
                Console.WriteLine($"🔥 AUDIO DEBUG: Auto-committing after {_sessionChunkCounts[connectionId]} chunks for {connectionId}");
                CommitAudioFileInternal(connectionId, $"auto_commit_{_sessionChunkCounts[connectionId]}_chunks");
            }
        }
    }

    public void CommitAudioFile(string connectionId, string reason = "utterance_complete")
    {
        lock (_lock)
        {
            CommitAudioFileInternal(connectionId, reason);
        }
    }

    private void CommitAudioFileInternal(string connectionId, string reason)
    {
        if (!_sessionAudioBuffers.ContainsKey(connectionId) || !_sessionAudioBuffers[connectionId].Any())
        {
            Console.WriteLine($"⚠️ AUDIO DEBUG: No audio data to commit for {connectionId} (Reason: {reason})");
            return;
        }

        try
        {
            var allAudioBytes = _sessionAudioBuffers[connectionId].SelectMany(chunk => chunk).ToArray();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"audio_{connectionId[^8..]}_{timestamp}_{reason}.wav";
            var filePath = Path.Combine(_outputDirectory, fileName);

            WriteWavFile(filePath, allAudioBytes, 16000, 1, 16);

            Console.WriteLine($"✅ AUDIO DEBUG: Saved audio file: {fileName}");
            Console.WriteLine($"   📊 Total chunks: {_sessionChunkCounts[connectionId]}");
            Console.WriteLine($"   📊 Total bytes: {allAudioBytes.Length}");
            Console.WriteLine($"   📊 Duration: {allAudioBytes.Length / (16000.0 * 2):F2}s (assuming 16kHz, 16-bit)");
            Console.WriteLine($"   📁 Path: {filePath}");

            // Clear the buffer after saving (only for manual commits, not auto-commits)
            if (reason == "utterance_complete" || reason == "no_transcript_detected")
            {
                _sessionAudioBuffers.Remove(connectionId);
                _sessionChunkCounts.Remove(connectionId);
                Console.WriteLine($"🧹 AUDIO DEBUG: Cleared session data for {connectionId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ AUDIO DEBUG: Error saving audio file for {connectionId}: {ex.Message}");
        }
    }

    private static void WriteWavFile(string filePath, byte[] audioData, int sampleRate, int channels, int bitsPerSample)
    {
        using var fileStream = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fileStream);

        // WAV Header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + audioData.Length); // File size minus 8 bytes
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // Format chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // PCM format chunk size
        writer.Write((short)1); // PCM format
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // Byte rate
        writer.Write((short)(channels * bitsPerSample / 8)); // Block align
        writer.Write((short)bitsPerSample);

        // Data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(audioData.Length);
        writer.Write(audioData);
    }

    public void CleanupSession(string connectionId)
    {
        lock (_lock)
        {
            if (_sessionAudioBuffers.ContainsKey(connectionId))
            {
                Console.WriteLine($"🧹 AUDIO DEBUG: Cleaning up session {connectionId} without committing");
                _sessionAudioBuffers.Remove(connectionId);
                _sessionChunkCounts.Remove(connectionId);
            }
        }
    }
}
