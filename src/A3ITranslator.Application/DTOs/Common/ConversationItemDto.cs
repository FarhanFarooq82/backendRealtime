using A3ITranslator.Application.DTOs.Audio;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.DTOs.Common;

/// <summary>
/// Conversation item for frontend notification system
/// Represents a complete conversational exchange with all associated data
/// </summary>
public class ConversationItem
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
    /// Detected or confirmed language of the original text
    /// </summary>
    public string DetectedLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Speaker information who provided the original text
    /// </summary>
    public SpeakerProfile Speaker { get; set; } = new();

    /// <summary>
    /// Type of response generated (Translation, AI Assistant, System)
    /// </summary>
    public string ResponseType { get; set; } = string.Empty;

    /// <summary>
    /// Generated response text (translation or AI response)
    /// </summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>
    /// Target language for the response
    /// </summary>
    public string ResponseLanguage { get; set; } = string.Empty;

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
    /// Confidence score for the original transcription (0.0 - 1.0)
    /// </summary>
    public float TranscriptionConfidence { get; set; }

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
/// TTS audio segment for streaming audio delivery
/// Contains audio data with associated metadata for frontend playback coordination
/// </summary>
public class TTSAudioSegment
{
    /// <summary>
    /// Raw audio data chunk (previously Base64 string)
    /// </summary>
    public byte[] AudioData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Text that this audio segment represents
    /// </summary>
    public string AssociatedText { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the first chunk in the audio stream
    /// </summary>
    public bool IsFirstChunk { get; set; }

    /// <summary>
    /// Sequence index of this chunk (0-based)
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Total number of chunks in the complete audio stream
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Conversation item ID this audio segment belongs to
    /// </summary>
    public string ConversationItemId { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the audio data (e.g., "audio/mp3", "audio/wav")
    /// </summary>
    public string MimeType { get; set; } = "audio/mp3";

    /// <summary>
    /// Duration of this audio segment in milliseconds
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Whether this is the final chunk in the stream
    /// </summary>
    public bool IsEndOfStream => ChunkIndex == TotalChunks - 1;
}
