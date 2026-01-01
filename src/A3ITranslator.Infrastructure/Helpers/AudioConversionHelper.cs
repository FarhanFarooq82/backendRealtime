using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace A3ITranslator.Infrastructure.Helpers;

/// <summary>
/// Helper class for audio format conversion
/// </summary>
public static class AudioConversionHelper
{
    /// <summary>
    /// Convert any audio format to WAV using FFmpeg
    /// </summary>
    public static async Task<string> ConvertToWavWithFFmpeg(byte[] audio, CancellationToken cancellationToken)
    {
        // Validate audio data
        if (audio == null || audio.Length == 0)
        {
            throw new ArgumentException("Audio data is null or empty");
        }
        
        if (audio.Length < 100)
        {
            throw new ArgumentException($"Audio data too small ({audio.Length} bytes)");
        }
        
        // Create temporary files
        string tempInputFile = Path.GetTempFileName();
        string tempWavFile = Path.GetTempFileName().Replace(".tmp", ".wav");
        
        try
        {
            // Write audio data to temporary input file
            await File.WriteAllBytesAsync(tempInputFile, audio, cancellationToken);

            // Convert to WAV using FFmpeg
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{tempInputFile}\" -ar 16000 -ac 1 -f wav \"{tempWavFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                throw new Exception("Failed to start FFmpeg process");
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(tempWavFile))
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                throw new Exception($"FFmpeg conversion failed. Exit code: {process.ExitCode}. Error: {stderr}");
            }

            // Verify output file
            var outputInfo = new FileInfo(tempWavFile);
            if (outputInfo.Length == 0)
            {
                throw new Exception("FFmpeg produced empty WAV file");
            }
            
            return tempWavFile;
        }
        catch (Exception ex)
        {
            throw new Exception($"Audio conversion failed: {ex.Message}");
        }
        finally
        {
            // Clean up input file
            try { if (File.Exists(tempInputFile)) File.Delete(tempInputFile); } catch { }
        }
    }
}
