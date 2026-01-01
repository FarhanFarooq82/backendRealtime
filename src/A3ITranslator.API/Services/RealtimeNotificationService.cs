// Create A3ITranslator.API/Services/RealtimeNotificationService.cs
using A3ITranslator.Application.Services;
using Microsoft.AspNetCore.SignalR;
using A3ITranslator.API.Hubs;
using A3ITranslator.Application.Interfaces;
using A3ITranslator.Application.DTOs.Speaker;

namespace A3ITranslator.API.Services;

public class RealtimeNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<AudioConversationHub, IAudioClient> _hubContext;

    public RealtimeNotificationService(IHubContext<AudioConversationHub, IAudioClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal)
    {
        await _hubContext.Clients.Client(connectionId).ReceiveTranscription(text, language, isFinal);
    }

    public async Task NotifyLanguageDetectedAsync(string connectionId, string language)
    {
        // TODO: Implement when needed
        await Task.CompletedTask;
    }

    public async Task NotifyErrorAsync(string connectionId, string error)
    {
        await _hubContext.Clients.Client(connectionId).ReceiveError(error);
    }

    public async Task NotifyAudioChunkAsync(string connectionId, string base64Audio)
    {
        await _hubContext.Clients.Client(connectionId).ReceiveAudioChunk(base64Audio);
    }

    public async Task NotifySpeakerUpdateAsync(string connectionId, SpeakerInfo speaker)
    {
        await _hubContext.Clients.Client(connectionId).ReceiveSpeakerUpdate(speaker);
    }

    public async Task NotifyTransactionCompleteAsync(string connectionId)
    {
        await _hubContext.Clients.Client(connectionId).ReceiveTransactionComplete();
    }
}