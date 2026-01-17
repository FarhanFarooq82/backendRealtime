
using A3ITranslator.Application.DTOs.GenAI;

namespace A3ITranslator.Application.DTOs.Audio;

/// <summary>
/// Audio processing response DTO
/// </summary>
public class AudioResponseDto
{
    public string Transcription { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public bool IsDirectQuery { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? AudioType { get; set; }
    public GenAIResponseDto? GenAIResponse { get; set; }
}
