using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class AudioTestCollector
{
    private readonly ILogger<AudioTestCollector> _logger;

    private readonly ConcurrentBag<byte[]> _pcmChunks = new();
    private Process? _ffmpeg;
    private bool _isCollecting = false;
    private int _chunkCount = 0;
    private DateTime _startTime;

    public AudioTestCollector(ILogger<AudioTestCollector> logger)
    {
        _logger = logger;
    }

    // ------------------------------------------------------------
    // START COLLECTION
    // ------------------------------------------------------------
    public void StartCollection(string sessionId)
    {
        if (_isCollecting)
            return;

        _isCollecting = true;
        _chunkCount = 0;
        _pcmChunks.Clear();
        _startTime = DateTime.UtcNow;

        Console.WriteLine($"🎙️ AUDIO TEST: Starting collection for session {sessionId}");

        StartFfmpegProcess();
    }

    // ------------------------------------------------------------
    // ADD CHUNK (Enhanced payload with metadata from frontend)
    // FOR DIRECT WEBM/OPUS STREAMING TO GOOGLE STT
    // ------------------------------------------------------------
    public async Task AddChunk(dynamic enhancedPayload)
    {
        if (!_isCollecting || enhancedPayload == null)
            return;

        try
        {
            // Extract audio data and metadata from enhanced payload
            string audioData = "";
            var metadata = enhancedPayload?.metadata;
            
            // Handle different payload types (JsonElement vs dynamic)
            if (enhancedPayload is System.Text.Json.JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("audioData", out var audioDataProp))
                {
                    audioData = audioDataProp.ValueKind == System.Text.Json.JsonValueKind.String 
                        ? (audioDataProp.GetString() ?? "") 
                        : "";
                }
            }
            else
            {
                // Dynamic object
                audioData = enhancedPayload?.audioData?.ToString() ?? "";
            }
            
            if (string.IsNullOrEmpty(audioData))
            {
                Console.WriteLine($"❌ AUDIO TEST: Empty audio data in enhanced payload");
                return;
            }

            // Extract metadata for logging and tracking
            string sessionId = metadata?.sessionId?.ToString() ?? "unknown";
            string chunkId = metadata?.chunkId?.ToString() ?? "unknown";
            int sequence = metadata?.sequence ?? _chunkCount + 1;
            string timestamp = metadata?.timestamp?.ToString() ?? DateTime.UtcNow.ToString();
            string mimeType = metadata?.mimeType?.ToString() ?? "unknown";
            string checksum = metadata?.checksum?.ToString() ?? "none";
            string utteranceId = metadata?.utteranceId?.ToString() ?? "unknown";
            int size = metadata?.size ?? 0;

            // 🔍 CRITICAL: Decode base64 to get raw WebM/Opus bytes
            byte[] webmChunk = Convert.FromBase64String(audioData);
            
            _chunkCount++;
            Console.WriteLine($"🎙️ AUDIO TEST: Enhanced chunk #{_chunkCount}");
            Console.WriteLine($"   📊 Session: {sessionId}, ChunkId: {chunkId}, Sequence: {sequence}");
            Console.WriteLine($"   🎵 Audio: {audioData.Length} chars → {webmChunk.Length} bytes, MimeType: {mimeType}");
            Console.WriteLine($"   ⏰ Timestamp: {timestamp}, UtteranceId: {utteranceId}");
            Console.WriteLine($"   🔍 Checksum: {checksum}, OriginalSize: {size}");

            // 🎵 CRITICAL: For WebM/Opus direct streaming, we can send to Google STT directly
            // No need for FFmpeg conversion - Google STT supports WebM/Opus natively!
            Console.WriteLine($"✅ DIRECT WEBM: Ready to send {webmChunk.Length} bytes to Google STT (no conversion needed)");

            // Optional: Still write to FFmpeg for debugging/comparison if process is running
            if (_ffmpeg?.HasExited == false)
            {
                await _ffmpeg.StandardInput.BaseStream.WriteAsync(webmChunk, 0, webmChunk.Length);
                await _ffmpeg.StandardInput.BaseStream.FlushAsync();
                Console.WriteLine($"📤 FFMPEG DEBUG: Also sent to FFmpeg for comparison");
            }
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"❌ Base64 decode error for enhanced chunk #{_chunkCount}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing enhanced chunk #{_chunkCount}: {ex.Message}");
        }
    }

    // Fallback method for simple base64 string (backward compatibility)
    // FOR DIRECT WEBM/OPUS STREAMING TO GOOGLE STT
    public async Task AddChunk(string base64AudioChunk)
    {
        if (!_isCollecting || string.IsNullOrEmpty(base64AudioChunk))
            return;

        try
        {
            // 🔍 CRITICAL: Decode base64 to get raw WebM/Opus bytes
            byte[] webmChunk = Convert.FromBase64String(base64AudioChunk);
            
            _chunkCount++;
            Console.WriteLine($"🎙️ AUDIO TEST: Simple chunk #{_chunkCount} (base64: {base64AudioChunk.Length} chars → WebM: {webmChunk.Length} bytes)");

            // 🎵 CRITICAL: For WebM/Opus direct streaming, we can send to Google STT directly
            // No need for FFmpeg conversion - Google STT supports WebM/Opus natively!
            Console.WriteLine($"✅ DIRECT WEBM: Ready to send {webmChunk.Length} bytes to Google STT (no conversion needed)");

            // Optional: Still write to FFmpeg for debugging/comparison if process is running
            if (_ffmpeg?.HasExited == false)
            {
                await _ffmpeg.StandardInput.BaseStream.WriteAsync(webmChunk, 0, webmChunk.Length);
                await _ffmpeg.StandardInput.BaseStream.FlushAsync();
                Console.WriteLine($"📤 FFMPEG DEBUG: Also sent to FFmpeg for comparison");
            }
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"❌ Base64 decode error for chunk #{_chunkCount}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing chunk #{_chunkCount}: {ex.Message}");
        }
    }

    // Overload for raw bytes (if needed for direct testing)
    public async Task AddChunk(byte[] webmChunk)
    {
        if (!_isCollecting || _ffmpeg == null || webmChunk.Length == 0)
            return;

        _chunkCount++;
        Console.WriteLine($"🎙️ AUDIO TEST: Received raw chunk #{_chunkCount} ({webmChunk.Length} bytes)");

        try
        {
            // Write raw WebM/Opus chunk into FFmpeg stdin
            await _ffmpeg.StandardInput.BaseStream.WriteAsync(webmChunk, 0, webmChunk.Length);
            await _ffmpeg.StandardInput.BaseStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FFmpeg write error: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    // STOP COLLECTION AND SAVE PCM AS WAV
    // ------------------------------------------------------------
    public void StopAndSave(string sessionId)
    {
        if (!_isCollecting)
            return;

        _isCollecting = false;

        try
        {
            _ffmpeg?.StandardInput.Close();
        }
        catch { }

        _ffmpeg?.WaitForExit();
        _ffmpeg?.Dispose();

        var filename = $"audio_test_final_{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav";
        SaveToFile(filename);

        Console.WriteLine($"🎙️ AUDIO TEST: Finished. Total chunks: {_chunkCount}");
    }

    // ------------------------------------------------------------
    // START FFMPEG PROCESS (persistent)
    // ------------------------------------------------------------
    private void StartFfmpegProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-hide_banner -loglevel error -f webm -i pipe:0 -ac 1 -ar 16000 -f s16le pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _ffmpeg = new Process { StartInfo = psi };
        _ffmpeg.Start();

        // Read PCM output continuously
        Task.Run(async () =>
        {
            var buffer = new byte[4096];
            var stdout = _ffmpeg.StandardOutput.BaseStream;

            while (_isCollecting)
            {
                int read = await stdout.ReadAsync(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    var pcm = new byte[read];
                    Buffer.BlockCopy(buffer, 0, pcm, 0, read);
                    _pcmChunks.Add(pcm);
                }
                else break;
            }
        });
    }

    // ------------------------------------------------------------
    // SAVE PCM TO WAV FILE
    // ------------------------------------------------------------
    private void SaveToFile(string filename)
    {
        try
        {
            var debugDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "A3I_Audio_Debug");

            Directory.CreateDirectory(debugDir);

            var filepath = Path.Combine(debugDir, filename);

            var totalBytes = _pcmChunks.Sum(c => c.Length);
            var combined = new byte[totalBytes];

            int offset = 0;
            foreach (var chunk in _pcmChunks)
            {
                Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                offset += chunk.Length;
            }

            var header = CreateWavHeader(totalBytes);

            using var fs = new FileStream(filepath, FileMode.Create);
            fs.Write(header, 0, header.Length);
            fs.Write(combined, 0, combined.Length);

            Console.WriteLine($"✅ Saved WAV: {filepath} ({totalBytes} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to save WAV: {ex.Message}");
        }
    }

    private byte[] CreateWavHeader(int dataLength)
    {
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        const short channels = 1;

        var header = new byte[44];
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = channels * bitsPerSample / 8;

        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, header, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(dataLength + 36), 0, header, 4, 4);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, header, 8, 4);

        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, header, 12, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(16), 0, header, 16, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, header, 20, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(channels), 0, header, 22, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, header, 24, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(byteRate), 0, header, 28, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((short)blockAlign), 0, header, 32, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(bitsPerSample), 0, header, 34, 2);

        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("data"), 0, header, 36, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(dataLength), 0, header, 40, 4);

        return header;
    }
}
