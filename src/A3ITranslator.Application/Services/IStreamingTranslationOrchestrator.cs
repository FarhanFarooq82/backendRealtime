using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Domain.Entities;

namespace A3ITranslator.Application.Services;

public interface IStreamingTranslationOrchestrator
{
    /// <summary>
    /// Processes streaming translation with real-time TTS and response type detection
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="transcriptionText">Final transcription from STT</param>
    /// <param name="speakerInfo">Speaker identification data</param>
    /// <param name="sessionContext">Current session context and history</param>
    /// <returns>Streaming operation result</returns>
    Task<StreamingTranslationResult> ProcessStreamingTranslationAsync(
        string sessionId, 
        string transcriptionText, 
        SpeakerProfile speakerInfo, 
        ConversationSession sessionContext);

 
}

public class StreamingTranslationResult
{
    public bool IsSuccess { get; set; }
    public string StreamingOperationId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ConversationHistoryUpdate
{
    public string SessionId { get; set; } = string.Empty;
    public List<SessionFact> ExtractedFacts { get; set; } = new();
    public bool IsReadyForNextCycle { get; set; }
}
