namespace A3ITranslator.Application.Models;

/// <summary>
/// TTS processing result with provider information
/// </summary>
public class TTSResult
{
    public bool Success { get; set; }
    public byte[]? AudioData { get; set; }
    public string? Provider { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsFallbackResult { get; set; }
    public float? Quality { get; set; }
}
