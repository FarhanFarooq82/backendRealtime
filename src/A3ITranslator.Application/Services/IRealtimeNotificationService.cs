using A3ITranslator.Application.DTOs.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Clean notification abstraction - no SignalR/API dependencies
/// </summary>
public interface IRealtimeNotificationService
{
    Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal);
    Task NotifyErrorAsync(string connectionId, string message);
    Task NotifyLanguageDetectedAsync(string connectionId, string language);
    Task NotifyAudioChunkAsync(string connectionId, string base64Audio);
    Task NotifySpeakerUpdateAsync(string connectionId, SpeakerInfo speaker);
    Task NotifyTransactionCompleteAsync(string connectionId);
}