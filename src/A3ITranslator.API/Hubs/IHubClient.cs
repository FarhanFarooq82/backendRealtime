using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.DTOs.Summary;

namespace A3ITranslator.API.Hubs;

/// <summary>
/// Client-side methods for SignalR Hub
/// </summary>
public interface IHubClient
{
    Task ReceiveTranscription(string text, string language, bool isFinal);
    Task ReceiveError(string message);
    
    // Processing state updates
    Task ReceiveProcessingStatus(string status);
    
    // Cycle completion
    Task ReceiveCycleCompletion(bool readyForNext);
    
    // Conversation items
    Task ReceiveTTSAudioSegment(TTSAudioSegment audioSegment);
    
    Task ReceiveFrontendSpeakerList(FrontendSpeakerListUpdate speakerList);
    
    // UI bubbles
    Task ReceiveFrontendConversationItem(FrontendConversationItem item);

    // Session management
    Task ReceiveStructuredSummary(SessionSummaryDTO summary);
    Task ReceiveFinalizationSuccess();
}
