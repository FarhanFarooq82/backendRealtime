using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;
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
    Task NotifyAudioChunkAsync(string connectionId, string base64Audio);
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
    /// Send progressive text updates during streaming
    /// </summary>
    Task SendProgressiveTextAsync(string sessionId, string textToken);

    /// <summary>
    /// Send audio chunks with associated text during streaming TTS
    /// </summary>
    Task SendAudioChunkAsync(string sessionId, AudioChunkData audioChunk);

    
    
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
    /// Send AI generated conversation summary for user review
    /// </summary>
    Task SendSessionSummaryAsync(string connectionId, string summaryText);

    /// <summary>
    /// Notify finalization success - signals all emails sent and session can be closed
    /// </summary>
    Task SendFinalizationSuccessAsync(string connectionId);
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
    public SpeakerProfile SpeakerAnalysis { get; set; } = new();
    public bool IsTransactionComplete { get; set; }
}

public class ConversationTurn
{
    public string TurnId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public SpeakerProfile Speaker { get; set; } = new();
    public ResponseType ResponseType { get; set; }
    public TimeSpan AudioDuration { get; set; }
    public List<string> AudioChunkUrls { get; set; } = new();
}