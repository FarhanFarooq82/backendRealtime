using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Domain.Entities;
using Microsoft.Extensions.Logging;
using A3ITranslator.Application.DTOs.Speaker;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

/// <summary>
/// Conversation orchestrator implementation
/// Manages the complete 3-stage conversation flow with proper error handling
/// </summary>
public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _logger;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly IStreamingTTSService _ttsService;
    private readonly ISessionRepository _sessionRepository;

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> logger,
        IRealtimeNotificationService notificationService,
        IStreamingTTSService ttsService,
        ISessionRepository sessionRepository)
    {
        _logger = logger;
        _notificationService = notificationService;
        _ttsService = ttsService;
        _sessionRepository = sessionRepository;
    }

    /// <summary>
    /// Stage 1: Process incoming transcription and determine response type
    /// </summary>
    public async Task<ConversationResult> ProcessTranscriptionAsync(
        string connectionId,
        string transcription,
        string detectedLanguage,
        float confidence,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎭 Stage 1: Processing transcription for {ConnectionId}", connectionId);

        try
        {
            // Get session context
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                return new ConversationResult
                {
                    Success = false,
                    ErrorMessage = "Session not found",
                    ErrorSeverity = ConversationErrorSeverity.Disruptive
                };
            }

            // Validate transcription quality
            if (confidence < 0.3f)
            {
                await HandleConversationErrorAsync(connectionId, 
                    "Low confidence transcription", 
                    ConversationErrorSeverity.Recoverable, 
                    cancellationToken);
                
                return new ConversationResult
                {
                    Success = false,
                    ErrorMessage = "Low confidence transcription",
                    ErrorSeverity = ConversationErrorSeverity.Recoverable
                };
            }

            // Notify VAD trigger completion
            await _notificationService.NotifyProcessingStatusAsync(connectionId, "Processing your request...");

            // Create conversation item
            var conversationItem = new ConversationItem
            {
                SessionId = session.SessionId,
                OriginalText = transcription,
                DetectedLanguage = detectedLanguage,
                TranscriptionConfidence = confidence,
                CreatedAt = DateTime.UtcNow,
                Speaker = new SpeakerInfo 
                { 
                    DisplayName = GetCurrentSpeakerName(session),
                    SpeakerId = session.CurrentSpeakerId ?? "unknown"
                }
            };

            // Determine response type and generate response
            var responseType = DetermineResponseType(transcription);
            conversationItem.ResponseType = responseType;

            _logger.LogInformation("🎯 Stage 1: Response type determined as {ResponseType}", responseType);

            // Process with translation orchestrator
            var generatedResponse = await GenerateResponseAsync(transcription, detectedLanguage, responseType);
            conversationItem.ResponseText = generatedResponse.response;
            conversationItem.ResponseLanguage = generatedResponse.targetLanguage;
            conversationItem.VoiceName = generatedResponse.voiceName;

            return new ConversationResult
            {
                Success = true,
                ConversationItem = conversationItem
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Stage 1 error for {ConnectionId}", connectionId);
            
            await HandleConversationErrorAsync(connectionId, 
                ex.Message, 
                ConversationErrorSeverity.Disruptive, 
                cancellationToken);

            return new ConversationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ErrorSeverity = ConversationErrorSeverity.Disruptive
            };
        }
    }

    /// <summary>
    /// Stage 2: Process GenAI response and start TTS synthesis
    /// </summary>
    public async Task<bool> ProcessGeneratedResponseAsync(
        string connectionId,
        ConversationItem conversationItem,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎭 Stage 2: Processing generated response for {ConnectionId}", connectionId);

        try
        {
            // Send conversation item to frontend
            await _notificationService.SendConversationItemAsync(connectionId, conversationItem);

            // Start TTS streaming
            await _notificationService.NotifyProcessingStatusAsync(connectionId, "Generating speech...");

            var chunkCount = 0;
            var totalChunks = 0;
            var isFirstChunk = true;

            await foreach (var chunk in _ttsService.SynthesizeStreamAsync(
                conversationItem.ResponseText, 
                conversationItem.ResponseLanguage, 
                conversationItem.VoiceName, 
                cancellationToken))
            {
                var audioSegment = new TTSAudioSegment
                {
                    AudioData = Convert.ToBase64String(chunk.AudioData),
                    AssociatedText = chunk.AssociatedText,
                    IsFirstChunk = isFirstChunk,
                    ChunkIndex = chunkCount,
                    TotalChunks = chunk.TotalChunks,
                    ConversationItemId = conversationItem.Id,
                    MimeType = "audio/mp3"
                };

                // Send TTS audio segment
                await _notificationService.SendTTSAudioSegmentAsync(connectionId, audioSegment);

                chunkCount++;
                totalChunks = chunk.TotalChunks;
                isFirstChunk = false;

                _logger.LogTrace("🔊 Stage 2: Sent audio chunk {ChunkIndex}/{TotalChunks}", 
                    chunkCount, totalChunks);
            }

            conversationItem.IsCompleted = true;
            _logger.LogInformation("✅ Stage 2: TTS synthesis completed for {ConnectionId}", connectionId);

            // Proceed to Stage 3
            await CompleteConversationCycleAsync(connectionId, conversationItem.Id, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Stage 2 error for {ConnectionId}", connectionId);
            
            conversationItem.IsCompleted = false;
            conversationItem.ErrorMessage = ex.Message;

            await HandleConversationErrorAsync(connectionId, 
                ex.Message, 
                ConversationErrorSeverity.Recoverable, 
                cancellationToken);

            return false;
        }
    }

    /// <summary>
    /// Stage 3: Complete conversation cycle and prepare for next input
    /// </summary>
    public async Task CompleteConversationCycleAsync(
        string connectionId,
        string conversationItemId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎭 Stage 3: Completing conversation cycle for {ConnectionId}", connectionId);

        try
        {
            // Send cycle completion notification
            await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
            
            _logger.LogInformation("✅ Stage 3: Conversation cycle completed for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Stage 3 error for {ConnectionId}", connectionId);
            await HandleConversationErrorAsync(connectionId, ex.Message, ConversationErrorSeverity.Silent, cancellationToken);
        }
    }

    /// <summary>
    /// Handle conversation errors with appropriate severity-based responses
    /// </summary>
    public async Task HandleConversationErrorAsync(
        string connectionId,
        string error,
        ConversationErrorSeverity severity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError("🚨 Conversation error [{Severity}] for {ConnectionId}: {Error}", 
            severity, connectionId, error);

        try
        {
            switch (severity)
            {
                case ConversationErrorSeverity.Silent:
                    // Just log, don't notify user
                    _logger.LogWarning("Silent error: {Error}", error);
                    break;

                case ConversationErrorSeverity.Recoverable:
                    await _notificationService.NotifyProcessingErrorAsync(connectionId, 
                        "There was an issue processing your request. Please try again.");
                    break;

                case ConversationErrorSeverity.Disruptive:
                    await _notificationService.NotifyErrorAsync(connectionId, 
                        "Service temporarily unavailable. Please refresh and try again.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle conversation error for {ConnectionId}", connectionId);
        }
    }

    #region Private Helper Methods

    private string GetCurrentSpeakerName(ConversationSession session)
    {
        if (!string.IsNullOrEmpty(session.CurrentSpeakerId))
        {
            var speaker = session.GetSpeaker(session.CurrentSpeakerId);
            return speaker?.DisplayName ?? $"Speaker {session.CurrentSpeakerId}";
        }
        return "Unknown Speaker";
    }

    private string DetermineResponseType(string transcription)
    {
        // Simple heuristic - could be enhanced with ML
        var lowerText = transcription.ToLowerInvariant();
        
        if (lowerText.Contains("?") || 
            lowerText.StartsWith("what") || 
            lowerText.StartsWith("how") || 
            lowerText.StartsWith("why") ||
            lowerText.StartsWith("when") ||
            lowerText.StartsWith("where"))
        {
            return "AIAssistant";
        }

        return "Translation";
    }

    private async Task<(string response, string targetLanguage, string voiceName)> GenerateResponseAsync(
        string input, 
        string sourceLanguage, 
        string responseType)
    {
        // This would integrate with the existing translation orchestrator
        // For now, return a simple response
        var targetLanguage = sourceLanguage == "en-US" ? "es-ES" : "en-US";
        var response = responseType == "AIAssistant" 
            ? $"I understand your question: {input}" 
            : $"Translated: {input}";
        var voiceName = targetLanguage == "es-ES" ? "es-ES-ElviraNeural" : "en-US-JennyNeural";

        return (response, targetLanguage, voiceName);
    }

    #endregion
}
