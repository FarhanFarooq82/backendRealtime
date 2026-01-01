using A3ITranslator.Application.Enums;

namespace A3ITranslator.Application.DTOs.Audio;

/// <summary>
/// Audio processing request DTO
/// </summary>
public class AudioRequestDto
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string SessionId { get; set; } = string.Empty;
    public string MainLanguage { get; set; } = string.Empty;
    public string OtherLanguage { get; set; } = string.Empty;
    public RequestType RequestType { get; set; } = RequestType.Standard;
    public string ContentType { get; set; } = "audio/mp4";
}
