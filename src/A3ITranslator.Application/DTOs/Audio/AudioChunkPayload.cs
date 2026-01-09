namespace A3ITranslator.Application.DTOs.Audio;

/// <summary>
/// Simple DTO for frontend audio chunk payload
/// Matches frontend structure: { audioData: base64Audio, timestamp: chunk.timestamp }
/// </summary>
public class AudioChunkPayload
{
    public string AudioData { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}
