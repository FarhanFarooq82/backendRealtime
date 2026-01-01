namespace A3ITranslator.Application.Services;

/// <summary>
/// Clean STT processor interface - removed unused method
/// </summary>
public interface ISttProcessor
{
    Task StartSingleLanguageProcessingAsync(string connectionId, string language, CancellationToken cancellationToken);
    Task StartMultiLanguageDetectionAsync(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken);
}