using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

/// <summary>
/// TTS orchestrator interface for managing multiple TTS providers with fallback
/// </summary>
public interface ITTSOrchestrator
{
    /// <summary>
    /// Convert text to speech with fallback strategy
    /// Tries providers in order: Azure TTS -> Google TTS -> OpenAI TTS
    /// </summary>
    /// <param name="text">Text to convert to speech</param>
    /// <param name="language">Target language for TTS</param>
    /// <param name="sessionId">Session ID for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>TTS result with audio data and provider information</returns>
    Task<TTSResult> ConvertTextToSpeechWithFallbackAsync(
        string text, 
        string language, 
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convert text to speech with specific provider list
    /// </summary>
    /// <param name="text">Text to convert to speech</param>
    /// <param name="language">Target language for TTS</param>
    /// <param name="sessionId">Session ID for context</param>
    /// <param name="assignedTTSProviders">List of TTS providers to try in order</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>TTS result with audio data and provider information</returns>
    Task<TTSResult> ConvertTextToSpeechWithProvidersAsync(
        string text, 
        string language, 
        string sessionId,
        List<string> assignedTTSProviders,
        CancellationToken cancellationToken = default);
}
