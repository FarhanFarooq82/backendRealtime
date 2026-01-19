using A3ITranslator.Application.DTOs.Common;

namespace A3ITranslator.Application.Orchestration;

/// <summary>
/// Error severity levels for conversation orchestration
/// </summary>
public enum ConversationErrorSeverity
{
    /// <summary>
    /// Silent errors - logged but don't interrupt flow (e.g., minor quality issues)
    /// </summary>
    Silent,
    
    /// <summary>
    /// Recoverable errors - user is notified but conversation can continue (e.g., partial transcription failure)
    /// </summary>
    Recoverable,
    
    /// <summary>
    /// Disruptive errors - require immediate user attention and may reset conversation state (e.g., service unavailable)
    /// </summary>
    Disruptive
}

/// <summary>
/// Result of a conversation orchestration operation
/// </summary>
public class ConversationResult
{
    public bool Success { get; set; }
    public ConversationItem? ConversationItem { get; set; }
    public string? ErrorMessage { get; set; }
    public ConversationErrorSeverity ErrorSeverity { get; set; } = ConversationErrorSeverity.Silent;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Conversation orchestrator for managing the complete conversation lifecycle
/// Implements the 3-stage notification system:
/// 1. VAD trigger → User stops speaking detection
/// 2. GenAI complete → Translation/Response generated + TTS streaming
/// 3. Cycle complete → Ready for next user input
/// </summary>
public interface IConversationOrchestrator
{
    /// <summary>
    /// Stage 1: Process incoming audio transcription and determine response type
    /// Triggers VAD completion and starts GenAI processing
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="transcription">Transcribed user input</param>
    /// <param name="detectedLanguage">Auto-detected language</param>
    /// <param name="confidence">Transcription confidence score</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Conversation result with initial processing outcome</returns>
    Task<ConversationResult> ProcessTranscriptionAsync(
        string connectionId,
        string transcription,
        string detectedLanguage,
        float confidence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage 2: Process GenAI response and start TTS synthesis
    /// Generates ConversationItem and streams TTS audio segments
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="conversationItem">Complete conversation item</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success indicator for TTS processing</returns>
    Task<bool> ProcessGeneratedResponseAsync(
        string connectionId,
        ConversationItem conversationItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage 3: Complete conversation cycle and prepare for next input
    /// Sends cycle completion notification to frontend
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="conversationItemId">Completed conversation item ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CompleteConversationCycleAsync(
        string connectionId,
        string conversationItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle conversation errors with appropriate severity-based responses
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="error">Error message</param>
    /// <param name="severity">Error severity level</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleConversationErrorAsync(
        string connectionId,
        string error,
        ConversationErrorSeverity severity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enhanced pipeline entry point: Process raw audio chunk with speaker-aware pipeline
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="audioChunk">Raw audio chunk data</param>
    Task ProcessAudioChunkAsync(string connectionId, byte[] audioChunk);

    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// Triggers processing of the accumulated utterance through GenAI and TTS
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    Task CompleteUtteranceAsync(string connectionId);

    /// <summary>
    /// Initialize connection pipeline with language candidates
    /// Prepares STT, Speaker, and VAD processing for incoming audio
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="candidateLanguages">Language candidates for auto-detection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeConnectionPipeline(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken);

    /// <summary>
    /// Cleanup connection state and speaker profiles
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    Task CleanupConnection(string connectionId);
}
