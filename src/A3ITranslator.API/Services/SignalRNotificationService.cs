using A3ITranslator.Application.Services;

using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.DTOs.Summary;
using A3ITranslator.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace A3ITranslator.API.Services;

/// <summary>
/// SignalR implementation - stays in API layer where it belongs
/// </summary>
public class SignalRNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<HubClient, IHubClient> _hubContext;

    public SignalRNotificationService(IHubContext<HubClient, IHubClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal) =>
        _hubContext.Clients.Client(connectionId).ReceiveTranscription(text, language, isFinal);

    public Task NotifyErrorAsync(string connectionId, string message) =>
        _hubContext.Clients.Client(connectionId).ReceiveError(message);

    // ✅ NEW: Processing state updates  
    public Task NotifyProcessingStatusAsync(string connectionId, string status) =>
        _hubContext.Clients.Client(connectionId).ReceiveProcessingStatus(status);


    public Task SendCycleCompletionAsync(string sessionId, bool readyForNext) =>
        _hubContext.Clients.Client(sessionId).ReceiveCycleCompletion(readyForNext);
    public Task SendTTSAudioSegmentAsync(string connectionId, TTSAudioSegment audioSegment) =>
        _hubContext.Clients.Client(connectionId).ReceiveTTSAudioSegment(audioSegment);

    public Task NotifyCycleCompletionAsync(string connectionId, bool readyForNext) =>
        _hubContext.Clients.Client(connectionId).ReceiveCycleCompletion(readyForNext);

    public Task SendFrontendSpeakerListAsync(string connectionId, FrontendSpeakerListUpdate speakerList) =>
        _hubContext.Clients.Client(connectionId).ReceiveFrontendSpeakerList(speakerList);

    public Task SendFrontendConversationItemAsync(string connectionId, FrontendConversationItem item) =>
        _hubContext.Clients.Client(connectionId).ReceiveFrontendConversationItem(item);

    public Task SendStructuredSummaryAsync(string connectionId, SessionSummaryDTO summary) =>
        _hubContext.Clients.Client(connectionId).ReceiveStructuredSummary(summary);

    public Task SendFinalizationSuccessAsync(string connectionId) =>
        _hubContext.Clients.Client(connectionId).ReceiveFinalizationSuccess();
}