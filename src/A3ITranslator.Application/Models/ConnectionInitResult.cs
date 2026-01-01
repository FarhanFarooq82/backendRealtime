namespace A3ITranslator.Application.Models;

public class ConnectionInitResult
{
    public string SessionId { get; set; } = string.Empty;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public string SecondaryLanguage { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}