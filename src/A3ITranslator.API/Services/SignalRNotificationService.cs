using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.Interfaces;
using A3ITranslator.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace A3ITranslator.API.Services;

/// <summary>
/// SignalR implementation - stays in API layer where it belongs
/// </summary>
public class SignalRNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<AudioConversationHub, IAudioClient> _hubContext;

    public SignalRNotificationService(IHubContext<AudioConversationHub, IAudioClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal) =>
        _hubContext.Clients.Client(connectionId).ReceiveTranscription(text, language, isFinal);

    public Task NotifyErrorAsync(string connectionId, string message) =>
        _hubContext.Clients.Client(connectionId).ReceiveError(message);

    public Task NotifyLanguageDetectedAsync(string connectionId, string language) =>
        _hubContext.Clients.Client(connectionId).ReceiveDominantLanguageDetected(language);

    public Task NotifyAudioChunkAsync(string connectionId, string base64Audio) =>
        _hubContext.Clients.Client(connectionId).ReceiveAudioChunk(base64Audio);

    public Task NotifySpeakerUpdateAsync(string connectionId, SpeakerInfo speaker) =>
        _hubContext.Clients.Client(connectionId).ReceiveSpeakerUpdate(speaker);

    public Task NotifyTransactionCompleteAsync(string connectionId) =>
        _hubContext.Clients.Client(connectionId).ReceiveTransactionComplete();
}