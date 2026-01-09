using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Clean notification abstraction - no SignalR/API dependencies
/// Enhanced with state management for better UX
/// </summary>
public interface IRealtimeNotificationService
{
    Task NotifyTranscriptionAsync(string connectionId, string text, string language, bool isFinal);
    Task NotifyErrorAsync(string connectionId, string message);
    Task NotifyLanguageDetectedAsync(string connectionId, string language);
    Task NotifyAudioChunkAsync(string connectionId, string base64Audio);
    Task NotifyTranslationAsync(string connectionId, string text, string language, bool isFinal);
    Task NotifySpeakerUpdateAsync(string connectionId, SpeakerListUpdate speakerUpdate);
    Task NotifyTransactionCompleteAsync(string connectionId);

    // ✅ NEW: Audio reception state management
    /// <summary>
    /// Notify that audio was successfully received and processing started
    /// Frontend should stop recording and show processing status
    /// </summary>
    Task NotifyAudioReceptionAckAsync(string connectionId, string message);

    /// <summary>
    /// Notify that audio reception failed (empty, error, etc.)
    /// Frontend should stop recording and ask user to try again
    /// </summary>
    Task NotifyAudioReceptionErrorAsync(string connectionId, string errorMessage);

    // ✅ NEW: Processing state updates
    /// <summary>
    /// Update processing status (e.g., "Generating response...", "Preparing speech...")
    /// </summary>
    Task NotifyProcessingStatusAsync(string connectionId, string status);

    /// <summary>
    /// Notify processing error - keeps session active for retry
    /// </summary>
    Task NotifyProcessingErrorAsync(string connectionId, string errorMessage);

    // ✅ NEW: Streaming translation capabilities
    /// <summary>
    /// Notify frontend of detected response type (AI Assistant vs Translation)
    /// </summary>
    Task SendResponseTypeAsync(string sessionId, ResponseType responseType);

    /// <summary>
    /// Send progressive text updates during streaming
    /// </summary>
    Task SendProgressiveTextAsync(string sessionId, string textToken);

    /// <summary>
    /// Send audio chunks with associated text during streaming TTS
    /// </summary>
    Task SendAudioChunkAsync(string sessionId, AudioChunkData audioChunk);

    /// <summary>
    /// Send final conversation history and extracted data
    /// </summary>
    Task SendConversationHistoryAsync(string sessionId, ConversationHistoryData historyData);

    /// <summary>
    /// Notify that streaming operation was cancelled
    /// </summary>
    Task SendOperationCancelledAsync(string sessionId);

    /// <summary>
    /// Send cycle completion confirmation - frontend can start next audio cycle
    /// </summary>
    Task SendCycleCompletionAsync(string sessionId, bool readyForNext);

    // ✅ NEW: Conversation orchestration methods
    /// <summary>
    /// Send complete conversation item to frontend
    /// </summary>
    Task SendConversationItemAsync(string connectionId, ConversationItem conversationItem);

    /// <summary>
    /// Send TTS audio segment during streaming synthesis
    /// </summary>
    Task SendTTSAudioSegmentAsync(string connectionId, TTSAudioSegment audioSegment);

    /// <summary>
    /// Notify cycle completion - frontend can start next audio cycle
    /// </summary>
    Task NotifyCycleCompletionAsync(string connectionId, bool readyForNext);
}

// Supporting enums and types for the interface
public enum ResponseType
{
    DirectTranslation,
    AIAssistantResponse,
    SystemNotification
}

public class AudioChunkData
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string AssociatedText { get; set; } = string.Empty;
    public bool IsFirstChunk { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
}

public class ConversationHistoryData
{
    public ConversationTurn ConversationTurn { get; set; } = new();
    public List<SessionFact> ExtractedFacts { get; set; } = new();
    public SpeakerInfo SpeakerAnalysis { get; set; } = new();
    public bool IsTransactionComplete { get; set; }
}

public class ConversationTurn
{
    public string TurnId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public SpeakerInfo Speaker { get; set; } = new();
    public ResponseType ResponseType { get; set; }
    public TimeSpan AudioDuration { get; set; }
    public List<string> AudioChunkUrls { get; set; } = new();
}