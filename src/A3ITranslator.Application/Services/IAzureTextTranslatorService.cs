namespace A3ITranslator.Application.Services;

public interface IAzureTextTranslatorService
{
    Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage, CancellationToken cancellationToken = default);
}
