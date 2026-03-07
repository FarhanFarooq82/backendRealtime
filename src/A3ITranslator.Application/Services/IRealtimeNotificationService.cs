using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.DTOs.Summary;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Clean notification abstraction - no SignalR/API dependencies
/// Enhanced with state management for better UX
/// </summary>
public interface IRealtimeNotificationService
{
    Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal);
    Task NotifyErrorAsync(string connectionId, string message);
    // ✅ NEW: Processing state updates
    /// <summary>
    /// Update processing status (e.g., "Generating response...", "Preparing speech...")
    /// </summary>
    Task NotifyProcessingStatusAsync(string connectionId, string status);


    /// <summary>
    /// Send TTS audio segment during streaming synthesis
    /// </summary>
    Task SendTTSAudioSegmentAsync(string connectionId, TTSAudioSegment audioSegment);

    /// <summary>
    /// Notify cycle completion - frontend can start next audio cycle
    /// </summary>
    Task NotifyCycleCompletionAsync(string connectionId, bool readyForNext);

    // ✅ NEW: Frontend-specific simplified DTOs
    /// <summary>
    /// Send speaker list update with simplified frontend DTOs
    /// Sent after speaker identification and update steps finish
    /// </summary>
    Task SendFrontendSpeakerListAsync(string connectionId, FrontendSpeakerListUpdate speakerList);

    /// <summary>
    /// Send the final conversation UI bubble explicitly meant for the frontend display
    /// </summary>
    Task SendFrontendConversationItemAsync(string connectionId, FrontendConversationItem item);

    /// <summary>
    /// Send structured bilingual summary with RTL support and metadata
    /// </summary>
    Task SendStructuredSummaryAsync(string connectionId, SessionSummaryDTO summary);

    /// <summary>
    /// Notify finalization success - signals all emails sent and session can be closed
    /// </summary>
    Task SendFinalizationSuccessAsync(string connectionId);

    // Supporting enums and types for the interface
    public enum ResponseType
    {
        DirectTranslation,
        AIAssistantResponse,
        SystemNotification
    }
}