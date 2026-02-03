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
    Task NotifyAudioChunkAsync(string connectionId, byte[] audioChunk);
    Task NotifyTranslationAsync(string connectionId, string text, string language, bool isFinal);
    Task NotifyTransactionCompleteAsync(string connectionId);


    // ✅ NEW: Processing state updates
    /// <summary>
    /// Update processing status (e.g., "Generating response...", "Preparing speech...")
    /// </summary>
    Task NotifyProcessingStatusAsync(string connectionId, string status);

    /// <summary>
    /// Notify processing error - keeps session active for retry
    /// </summary>
    Task NotifyProcessingErrorAsync(string connectionId, string errorMessage);


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
    /// Send conversation item with essential frontend data
    /// Reusable method for translation and AI responses
    /// </summary>
    Task SendFrontendConversationItemAsync(string connectionId, FrontendConversationItem conversationItem);

    /// <summary>
    /// Send TTS audio chunk for frontend playback
    /// </summary>
    Task SendFrontendTTSChunkAsync(string connectionId, FrontendTTSChunk ttsChunk);

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