namespace A3ITranslator.Application.Services;

/// <summary>
/// Simple interface for language service - only STT languages
/// </summary>
public interface ILanguageService
{
    /// <summary>
    /// Get all supported STT languages combined from all providers
    /// </summary>
    Dictionary<string, string> GetAllSupportedLanguages();
}
