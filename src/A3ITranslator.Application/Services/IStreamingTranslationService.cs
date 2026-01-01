namespace A3ITranslator.Application.Services;

public interface IStreamingTranslationService
{
    Task<string> TranslateStreamAsync(string text, string sourceLanguage, string targetLanguage);
}
