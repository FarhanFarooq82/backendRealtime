namespace A3ITranslator.Application.DTOs.Audio;

/// <summary>
/// Simple DTO for frontend audio chunk payload
/// Matches frontend structure: { audioData: base64Audio, timestamp: chunk.timestamp }
/// </summary>
public class AudioChunkPayload
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public long Timestamp { get; set; }
}
