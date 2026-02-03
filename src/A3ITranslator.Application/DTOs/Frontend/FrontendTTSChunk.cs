namespace A3ITranslator.Application.DTOs.Frontend;

/// <summary>
/// TTS audio chunk for frontend audio playback
/// Contains audio data, metadata, and associated text for streaming playback
/// </summary>
public class FrontendTTSChunk
{
    /// <summary>
    /// Unique identifier for this audio chunk
    /// </summary>
    public string ChunkId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Conversation item ID this chunk belongs to
    /// </summary>
    public string ConversationItemId { get; set; } = string.Empty;

    /// <summary>
    /// Raw audio data (previously Base64 string)
    /// </summary>
    public byte[] AudioData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Text that this audio chunk represents
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Sequence number of this chunk (0-based)
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Total number of chunks in the complete audio stream
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Whether this is the first chunk in the sequence
    /// </summary>
    public bool IsFirstChunk { get; set; }

    /// <summary>
    /// Whether this is the final chunk in the sequence
    /// </summary>
    public bool IsLastChunk { get; set; }

    /// <summary>
    /// Audio format/MIME type (e.g., "audio/mp3", "audio/wav")
    /// </summary>
    public string AudioFormat { get; set; } = "audio/mp3";

    /// <summary>
    /// Duration of this audio chunk in milliseconds
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Timestamp when this chunk was generated
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Sample rate of the audio data
    /// </summary>
    public int SampleRate { get; set; } = 24000;

    /// <summary>
    /// Audio quality/bitrate metadata
    /// </summary>
    public string Quality { get; set; } = "standard";
}
