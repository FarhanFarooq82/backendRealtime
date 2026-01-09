namespace A3ITranslator.Application.DTOs.Common;

/// <summary>
/// Enhanced conversation item with detailed confidence metrics for all processing stages
/// </summary>
public class EnhancedConversationItem
{
    /// <summary>
    /// Unique identifier for the conversation item
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Session identifier this conversation belongs to
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Original transcribed text from the user
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Generated response text (translation or AI response)
    /// </summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>
    /// Source language of the original text
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Target language for the response
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Enhanced speaker information with detailed confidence metrics
    /// </summary>
    public Speaker.EnhancedSpeakerInfo Speaker { get; set; } = new();

    /// <summary>
    /// Detailed confidence metrics for all processing stages
    /// </summary>
    public ConversationConfidenceMetrics ConfidenceMetrics { get; set; } = new();

    /// <summary>
    /// Type of response generated (Translation, AI Assistant, System)
    /// </summary>
    public string ResponseType { get; set; } = "Translation";

    /// <summary>
    /// Voice name used for TTS synthesis
    /// </summary>
    public string VoiceName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the conversation item was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public double ProcessingDurationMs { get; set; }

    /// <summary>
    /// Whether this conversation item completed successfully
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Additional metadata for the conversation item
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Detailed confidence metrics for conversation processing
/// </summary>
public class ConversationConfidenceMetrics
{
    /// <summary>
    /// Speech-to-text transcription confidence (0-100%)
    /// </summary>
    public float TranscriptionConfidence { get; set; }

    /// <summary>
    /// Language detection confidence (0-100%)
    /// </summary>
    public float LanguageDetectionConfidence { get; set; }

    /// <summary>
    /// Speaker identification confidence (0-100%)
    /// </summary>
    public float SpeakerIdentificationConfidence { get; set; }

    /// <summary>
    /// Translation quality confidence (0-100%)
    /// </summary>
    public float TranslationConfidence { get; set; }

    /// <summary>
    /// Overall combined confidence score (0-100%)
    /// </summary>
    public float OverallConfidence => 
        (TranscriptionConfidence + LanguageDetectionConfidence + 
         SpeakerIdentificationConfidence + TranslationConfidence) / 4f;

    /// <summary>
    /// Quality assessment based on confidence levels
    /// </summary>
    public QualityLevel QualityAssessment 
    { 
        get
        {
            var overall = OverallConfidence;
            return overall switch
            {
                >= 85 => QualityLevel.Excellent,
                >= 70 => QualityLevel.Good,
                >= 55 => QualityLevel.Fair,
                >= 40 => QualityLevel.Poor,
                _ => QualityLevel.Unreliable
            };
        }
    }

    /// <summary>
    /// Detailed breakdown of confidence factors
    /// </summary>
    public Dictionary<string, float> DetailedFactors { get; set; } = new();
}

/// <summary>
/// Quality assessment levels for conversation processing
/// </summary>
public enum QualityLevel
{
    Unreliable = 0,  // < 40%
    Poor = 1,        // 40-55%
    Fair = 2,        // 55-70%
    Good = 3,        // 70-85%
    Excellent = 4    // 85%+
}

/// <summary>
/// Real-time confidence update for live transcription display
/// </summary>
public class LiveConfidenceUpdate
{
    /// <summary>
    /// Current partial transcription text
    /// </summary>
    public string PartialText { get; set; } = string.Empty;

    /// <summary>
    /// Real-time confidence for the partial transcription
    /// </summary>
    public float PartialConfidence { get; set; }

    /// <summary>
    /// Detected language with confidence
    /// </summary>
    public string DetectedLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Language detection confidence
    /// </summary>
    public float LanguageConfidence { get; set; }

    /// <summary>
    /// Provisional speaker identification (may change)
    /// </summary>
    public string ProvisionalSpeakerId { get; set; } = string.Empty;

    /// <summary>
    /// Speaker identification confidence
    /// </summary>
    public float SpeakerConfidence { get; set; }

    /// <summary>
    /// Whether this is a final result or still processing
    /// </summary>
    public bool IsFinal { get; set; }

    /// <summary>
    /// Timestamp of this update
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
