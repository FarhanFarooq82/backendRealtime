using A3ITranslator.Application.Enums;

namespace A3ITranslator.Application.DTOs.Audio;

/// <summary>
/// STT processing result with language detection and speaker identification
/// </summary>
public class STTResult
{
    public bool Success { get; set; }
    public string Transcription { get; set; } = string.Empty;
    public string DetectedLanguage { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string Provider { get; set; } = string.Empty;
    public double ProcessingTimeMs { get; set; }
    public bool IsFallbackResult { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    
    // Speaker identification
    public SpeakerAnalysis? SpeakerAnalysis { get; set; }
    public List<WordInfo> Words { get; set; } = new();
    
    // Legacy properties for backward compatibility
    public string Text => Transcription;
    public double LanguageConfidence => Confidence;
    public double TranscriptionConfidence => Confidence;
    public string ServiceName => Provider;
    public TimeSpan ProcessingTime => TimeSpan.FromMilliseconds(ProcessingTimeMs);
}

/// <summary>
/// Speaker analysis information from speech-to-text service
/// </summary>
public class SpeakerAnalysis
{
    public string? SpeakerIdentity { get; set; }
    public string? SpeakerLabel { get; set; }
    public int SpeakerTag { get; set; }
    public string? Gender { get; set; }
    public float Confidence { get; set; }

    /// <summary>
    /// Spectral centroid in Hz - voice brightness
    /// </summary>
    public float SpectralCentroid { get; set; }

    /// <summary>
    /// Voice energy in dB - typical volume level
    /// </summary>
    public float VoiceEnergy { get; set; }

    // NEW: Add missing properties that AudioController expects
    /// <summary>
    /// Primary/fundamental frequency in Hz
    /// Alias for FundamentalFrequency
    /// </summary>
    public float PrimaryFrequency { get; set; }

    /// <summary>
    /// Pitch variance (jitter) as percentage
    /// </summary>
    public float PitchVariance { get; set; }

    /// <summary>
    /// Language detected for this speaker
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Whether this is a known speaker from previous sessions
    /// </summary>
    public bool IsKnownSpeaker { get; set; }
}

/// <summary>
/// Word-level information with speaker diarization
/// </summary>
public class WordInfo
{
    public string Word { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public float Confidence { get; set; }
    public int SpeakerTag { get; set; }
    public string SpeakerLabel { get; set; } = string.Empty;
}


/// <summary>
/// Quality thresholds for STT processing
/// </summary>
public class STTQualityThresholds
{
    public float MinimumConfidence { get; set; } = 0.6f;
    public float PreferredConfidence { get; set; } = 0.8f;
    public int MaxRetryAttempts { get; set; } = 3;
}
