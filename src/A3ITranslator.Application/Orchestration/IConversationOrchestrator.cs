using A3ITranslator.Application.DTOs.Common;

namespace A3ITranslator.Application.Orchestration;

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
    /// Cancel the current conversation cycle and reset for next input
    /// Stops any active STT, GenAI, or TTS processing for this connection
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    Task CancelUtteranceAsync(string connectionId);

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

    /// <summary>
    /// Request an AI summary of the current session for user review
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    Task RequestSummaryAsync(string connectionId);

    /// <summary>
    /// Finalize the session, generate PDF transcript, and mail it to provided addresses
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="emailAddresses">List of email addresses to send the transcript to</param>
    Task FinalizeAndMailAsync(string connectionId, List<string> emailAddresses);
}
