using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;

namespace A3ITranslator.API.Hubs;

/// <summary>
/// Client-side methods for SignalR Hub
/// </summary>
public interface IHubClient
{
    Task ReceiveTranscription(string text, string language, bool isFinal);
    Task ReceiveError(string message);
    Task ReceiveAudioChunk(string base64Audio);
    Task ReceiveTranslation(string text, string language, bool isFinal);
    Task ReceiveTransactionComplete();
    
    // Audio reception state management
    Task ReceiveAudioReceptionAck(string message);
    Task ReceiveAudioReceptionError(string errorMessage);
    
    // Processing state updates
    Task ReceiveProcessingStatus(string status);
    Task ReceiveProcessingError(string errorMessage);
    
    // Cycle completion
    Task ReceiveCycleCompletion(bool readyForNext);
    
    // Conversation items
    Task ReceiveConversationItem(ConversationItem conversationItem);
    Task ReceiveTTSAudioSegment(TTSAudioSegment audioSegment);
    
    // Frontend-specific simplified DTOs
    Task ReceiveFrontendSpeakerList(FrontendSpeakerListUpdate speakerList);
    Task ReceiveFrontendConversationItem(FrontendConversationItem conversationItem);
    Task ReceiveFrontendTTSChunk(FrontendTTSChunk ttsChunk);
    
    // Session management
    Task ReceiveSessionSummary(string summaryText);
    Task ReceiveFinalizationSuccess();
}
