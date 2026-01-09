using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.DTOs.Common;

namespace A3ITranslator.Application.Interfaces; // Changed namespace

/// <summary>
/// Enhanced client interface with speaker support and state management
/// </summary>
public interface IHubClient
{
    Task ReceiveTranscription(string text, string language, bool isFinal);
    Task ReceiveAudioChunk(string base64Chunk);
    Task ReceiveTranslation(string text, string language, bool isFinal);
    Task ReceiveSpeakerUpdate(SpeakerListUpdate speakerUpdate);
    Task ReceiveTransactionComplete();
    Task ReceiveError(string message);
    
    // Add language detection method
    Task ReceiveDominantLanguageDetected(string dominantLanguage);

    // ✅ NEW: Audio reception state management
    /// <summary>
    /// Audio reception acknowledged - frontend should stop recording and show processing status
    /// </summary>
    Task ReceiveAudioReceptionAck(string message);

    /// <summary>
    /// Audio reception failed - frontend should stop recording and show error, ask to try again
    /// </summary>
    Task ReceiveAudioReceptionError(string errorMessage);

    // ✅ NEW: Processing state updates
    /// <summary>
    /// Processing status update (e.g., "Generating response...", "Preparing speech...")
    /// </summary>
    Task ReceiveProcessingStatus(string status);

    /// <summary>
    /// Processing error - show error but keep session active for retry
    /// </summary>
    Task ReceiveProcessingError(string errorMessage);

    // ✅ NEW: Streaming translation capabilities
    /// <summary>
    /// Receive detected response type (AI Assistant vs Translation)
    /// </summary>
    Task ReceiveResponseType(string responseType);

    /// <summary>
    /// Receive progressive text updates during streaming
    /// </summary>
    Task ReceiveProgressiveText(string textToken);

    /// <summary>
    /// Receive audio chunks with associated text during streaming TTS
    /// </summary>
    Task ReceiveAudioChunkWithText(string base64Audio, string associatedText, bool isFirstChunk, int chunkIndex, int totalChunks);

    /// <summary>
    /// Receive final conversation history and extracted data
    /// </summary>
    Task ReceiveConversationHistory(object historyData);

    /// <summary>
    /// Receive complete conversation item for frontend display
    /// </summary>
    Task ReceiveConversationItem(ConversationItem conversationItem);

    /// <summary>
    /// Receive TTS audio segment during streaming synthesis
    /// </summary>
    Task ReceiveTTSAudioSegment(TTSAudioSegment audioSegment);

    /// <summary>
    /// Receive operation cancelled notification
    /// </summary>
    Task ReceiveOperationCancelled();

    /// <summary>
    /// Receive cycle completion - frontend can start next audio cycle
    /// </summary>
    Task ReceiveCycleCompletion(bool readyForNext);
}