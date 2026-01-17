using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Complete notification abstraction - supports any transport layer
/// </summary>
public interface INotificationService
{
    // Basic notifications
    Task NotifyTranscription(string connectionId, string text, string language, bool isFinal);
    Task NotifyError(string connectionId, string message);
    Task NotifyLanguageDetected(string connectionId, string language);
    
    // Audio-specific notifications
    Task NotifyAudioChunk(string connectionId, string base64Audio);
    Task NotifyTransactionComplete(string connectionId);
    Task NotifySpeakerUpdate(string connectionId, SpeakerProfile speakerInfo);
}