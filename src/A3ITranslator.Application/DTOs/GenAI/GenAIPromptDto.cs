namespace A3ITranslator.Application.DTOs.GenAI;

/// <summary>
/// GenAI prompt request DTO
/// </summary>
public class GenAIPromptDto
{
    public string Prompt { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? Context { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}
