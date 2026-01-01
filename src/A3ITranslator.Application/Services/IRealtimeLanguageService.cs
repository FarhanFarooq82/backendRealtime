namespace A3ITranslator.Application.Services;

/// <summary>
/// Real-time language service for SignalR hub
/// </summary>
public interface IRealtimeLanguageService
{
    Task<IEnumerable<SupportedLanguage>> GetSupportedLanguagesAsync();
}

public class SupportedLanguage
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public bool IsSupported { get; set; } = true;
}