namespace A3ITranslator.Application.DTOs.GenAI;

/// <summary>
/// GenAI response DTO
/// </summary>
public class GenAIResponseDto
{
    public string Response { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
