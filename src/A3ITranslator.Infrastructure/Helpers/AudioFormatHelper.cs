using System;
using System.Linq;

namespace A3ITranslator.Infrastructure.Helpers;

/// <summary>
/// Helper class for audio format detection and validation
/// Supports common web browser audio formats
/// </summary>
public static class AudioFormatHelper
{
    /// <summary>
    /// Audio format detection result
    /// </summary>
    public class AudioFormatInfo
    {
        public string Format { get; set; } = "Unknown";
        public string MimeType { get; set; } = "audio/unknown";
        public bool IsValidAudio { get; set; } = false;
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Detect audio format from byte array header
    /// Supports common browser recording formats: WebM, OGG, MP4, WAV
    /// </summary>
    public static AudioFormatInfo DetectFormat(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 4)
        {
            return new AudioFormatInfo { Description = "Insufficient data for format detection" };
        }

        // WAV format detection (RIFF WAVE)
        if (audioData.Length >= 12)
        {
            var riffHeader = System.Text.Encoding.ASCII.GetString(audioData, 0, 4);
            var waveHeader = System.Text.Encoding.ASCII.GetString(audioData, 8, 4);
            
            if (riffHeader == "RIFF" && waveHeader == "WAVE")
            {
                return new AudioFormatInfo
                {
                    Format = "WAV",
                    MimeType = "audio/wav",
                    IsValidAudio = true,
                    Description = "WAV (RIFF WAVE) format"
                };
            }
        }

        // WebM format detection (Matroska/EBML header)
        if (audioData.Length >= 4)
        {
            var webmHeader = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
            var actualHeader = audioData.Take(4).ToArray();
            
            if (actualHeader.SequenceEqual(webmHeader))
            {
                return new AudioFormatInfo
                {
                    Format = "WebM",
                    MimeType = "audio/webm",
                    IsValidAudio = true,
                    Description = "WebM/Matroska format (common from Chrome/Edge)"
                };
            }
        }

        // OGG format detection
        if (audioData.Length >= 4)
        {
            var oggHeader = System.Text.Encoding.ASCII.GetString(audioData, 0, 4);
            
            if (oggHeader == "OggS")
            {
                return new AudioFormatInfo
                {
                    Format = "OGG",
                    MimeType = "audio/ogg",
                    IsValidAudio = true,
                    Description = "OGG format (common from Firefox)"
                };
            }
        }

        // MP4 format detection (multiple possible headers)
        if (audioData.Length >= 8)
        {
            // Check for 'ftyp' at offset 4 (MP4 container)
            var ftypHeader = System.Text.Encoding.ASCII.GetString(audioData, 4, 4);
            
            if (ftypHeader == "ftyp")
            {
                return new AudioFormatInfo
                {
                    Format = "MP4",
                    MimeType = "audio/mp4",
                    IsValidAudio = true,
                    Description = "MP4 format (common from Safari)"
                };
            }
        }

        // 3GP format detection (mobile browsers)
        if (audioData.Length >= 8)
        {
            var header = System.Text.Encoding.ASCII.GetString(audioData, 4, 4);
            
            if (header == "3gp4" || header == "3gp5" || header == "3gp6")
            {
                return new AudioFormatInfo
                {
                    Format = "3GP",
                    MimeType = "audio/3gpp",
                    IsValidAudio = true,
                    Description = "3GP format (mobile browsers)"
                };
            }
        }

        // If we reach here, format is unknown but might still be valid audio
        return new AudioFormatInfo
        {
            Format = "Unknown",
            MimeType = "audio/unknown",
            IsValidAudio = false,
            Description = $"Unknown format - First 8 bytes: {BitConverter.ToString(audioData.Take(8).ToArray())}"
        };
    }

    /// <summary>
    /// Quick validation if data appears to be audio
    /// </summary>
    public static bool IsLikelyAudioData(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 100) // Too small to be meaningful audio
            return false;

        var format = DetectFormat(audioData);
        return format.IsValidAudio;
    }

    /// <summary>
    /// Get a user-friendly format description
    /// </summary>
    public static string GetFormatDescription(byte[] audioData)
    {
        var format = DetectFormat(audioData);
        return $"{format.Format} ({format.Description})";
    }
}
