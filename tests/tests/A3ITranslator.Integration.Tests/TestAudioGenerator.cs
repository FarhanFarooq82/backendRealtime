using System.IO;
using System.Text;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Utility to create a simple test audio file for debugging purposes
/// This creates a minimal WAV file with sine wave audio for testing
/// </summary>
public static class TestAudioGenerator
{
    /// <summary>
    /// Generate a simple WAV file with a 440Hz tone for testing purposes
    /// </summary>
    public static byte[] GenerateTestWavFile(double durationSeconds = 2.0, int sampleRate = 16000)
    {
        var samples = (int)(sampleRate * durationSeconds);
        var frequency = 440.0; // A4 note
        
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);
        
        // WAV header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2); // File size - 8
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        
        // Format chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Format chunk size
        writer.Write((short)1); // PCM format
        writer.Write((short)1); // Mono
        writer.Write(sampleRate); // Sample rate
        writer.Write(sampleRate * 2); // Byte rate
        writer.Write((short)2); // Block align
        writer.Write((short)16); // Bits per sample
        
        // Data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2); // Data size
        
        // Generate sine wave samples
        for (int i = 0; i < samples; i++)
        {
            var sample = Math.Sin(2 * Math.PI * frequency * i / sampleRate);
            var intSample = (short)(sample * 32767 * 0.5); // 50% volume
            writer.Write(intSample);
        }
        
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Save a test audio file to the root directory for debugging
    /// </summary>
    public static async Task<string> SaveTestAudioFileAsync(string fileName = "test_audio.wav")
    {
        var audioData = GenerateTestWavFile();
        var filePath = Path.Combine("/Users/farhanfarooq/Documents/GitHub/A3ITranslator", fileName);
        
        await File.WriteAllBytesAsync(filePath, audioData);
        return filePath;
    }
}
