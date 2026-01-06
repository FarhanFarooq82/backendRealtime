using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.DTOs.Common;
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

    // ✅ NEW: Audio reception state management
    public Task NotifyAudioReceptionAckAsync(string connectionId, string message) =>
        _hubContext.Clients.Client(connectionId).ReceiveAudioReceptionAck(message);

    public Task NotifyAudioReceptionErrorAsync(string connectionId, string errorMessage) =>
        _hubContext.Clients.Client(connectionId).ReceiveAudioReceptionError(errorMessage);

    // ✅ NEW: Processing state updates  
    public Task NotifyProcessingStatusAsync(string connectionId, string status) =>
        _hubContext.Clients.Client(connectionId).ReceiveProcessingStatus(status);

    public Task NotifyProcessingErrorAsync(string connectionId, string errorMessage) =>
        _hubContext.Clients.Client(connectionId).ReceiveProcessingError(errorMessage);

    // ✅ NEW: Streaming translation capabilities
    public Task SendResponseTypeAsync(string sessionId, ResponseType responseType) =>
        _hubContext.Clients.Client(sessionId).ReceiveResponseType(responseType.ToString());

    public Task SendProgressiveTextAsync(string sessionId, string textToken) =>
        _hubContext.Clients.Client(sessionId).ReceiveProgressiveText(textToken);

    public Task SendAudioChunkAsync(string sessionId, AudioChunkData audioChunk) =>
        _hubContext.Clients.Client(sessionId).ReceiveAudioChunkWithText(
            Convert.ToBase64String(audioChunk.AudioData),
            audioChunk.AssociatedText,
            audioChunk.IsFirstChunk,
            audioChunk.ChunkIndex,
            audioChunk.TotalChunks);

    public Task SendConversationHistoryAsync(string sessionId, ConversationHistoryData historyData) =>
        _hubContext.Clients.Client(sessionId).ReceiveConversationHistory(historyData);

    public Task SendOperationCancelledAsync(string sessionId) =>
        _hubContext.Clients.Client(sessionId).ReceiveOperationCancelled();

    public Task SendCycleCompletionAsync(string sessionId, bool readyForNext) =>
        _hubContext.Clients.Client(sessionId).ReceiveCycleCompletion(readyForNext);

    // ✅ NEW: Conversation orchestration methods
    public Task SendConversationItemAsync(string connectionId, ConversationItem conversationItem) =>
        _hubContext.Clients.Client(connectionId).ReceiveConversationItem(conversationItem);

    public Task SendTTSAudioSegmentAsync(string connectionId, TTSAudioSegment audioSegment) =>
        _hubContext.Clients.Client(connectionId).ReceiveTTSAudioSegment(audioSegment);

    public Task NotifyCycleCompletionAsync(string connectionId, bool readyForNext) =>
        _hubContext.Clients.Client(connectionId).ReceiveCycleCompletion(readyForNext);
}