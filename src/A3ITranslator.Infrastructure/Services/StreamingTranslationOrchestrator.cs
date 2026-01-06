using System.Text;
using System.Text.Json;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.Domain.Entities;
using Microsoft.Extensions.Logging;
using ServiceConversationTurn = A3ITranslator.Application.Services.ConversationTurn;

namespace A3ITranslator.Infrastructure.Services;

public class StreamingTranslationOrchestrator : IStreamingTranslationOrchestrator
{
    private readonly IGenAIService _genAIService;
    private readonly IStreamingTTSService _ttsService;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ITranslationPromptService _promptService;
    private readonly ILogger<StreamingTranslationOrchestrator> _logger;
    
    // Active streaming operations tracking
    private readonly Dictionary<string, CancellationTokenSource> _activeOperations = new();
    private readonly Dictionary<string, StringBuilder> _streamAccumulators = new();
    private readonly object _operationsLock = new();

    public StreamingTranslationOrchestrator(
        IGenAIService genAIService,
        IStreamingTTSService ttsService,
        IRealtimeNotificationService notificationService,
        ITranslationPromptService promptService,
        ILogger<StreamingTranslationOrchestrator> logger)
    {
        _genAIService = genAIService;
        _ttsService = ttsService;
        _notificationService = notificationService;
        _promptService = promptService;
        _logger = logger;
    }

    public async Task<StreamingTranslationResult> ProcessStreamingTranslationAsync(
        string sessionId, 
        string transcriptionText, 
        SpeakerInfo speakerInfo, 
        ConversationSession sessionContext)
    {
        var operationId = Guid.NewGuid().ToString();
        var cancellationTokenSource = new CancellationTokenSource();
        
        lock (_operationsLock)
        {
            // Cancel any existing operation for this session
            if (_activeOperations.TryGetValue(sessionId, out var existingOperation))
            {
                existingOperation.Cancel();
                _activeOperations.Remove(sessionId);
            }
            
            _activeOperations[sessionId] = cancellationTokenSource;
            _streamAccumulators[sessionId] = new StringBuilder();
        }

        try
        {
            _logger.LogInformation("🔄 Starting streaming translation for session {SessionId}, operation {OperationId}", 
                sessionId, operationId);

            // Build enhanced translation request
            var request = new EnhancedTranslationRequest
            {
                Text = transcriptionText,
                SourceLanguage = "auto", // Auto-detect from context
                TargetLanguage = "en-US", // TODO: Get from session context
                SessionContext = new Dictionary<string, object>
                {
                    ["sessionId"] = sessionId,
                    ["speakerInfo"] = speakerInfo
                }
            };

            // Build comprehensive prompts
            var (systemPrompt, userPrompt) = _promptService.BuildTranslationPrompts(request);

            // Start streaming GenAI call
            var streamingTask = ProcessGenAIStreamAsync(
                sessionId, 
                systemPrompt, 
                userPrompt, 
                cancellationTokenSource.Token);

            await streamingTask;

            return new StreamingTranslationResult
            {
                IsSuccess = true,
                StreamingOperationId = operationId
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 Streaming operation cancelled for session {SessionId}", sessionId);
            return new StreamingTranslationResult
            {
                IsSuccess = false,
                ErrorMessage = "Operation was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in streaming translation for session {SessionId}", sessionId);
            return new StreamingTranslationResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            lock (_operationsLock)
            {
                _activeOperations.Remove(sessionId);
            }
        }
    }

    private async Task ProcessGenAIStreamAsync(
        string sessionId, 
        string systemPrompt, 
        string userPrompt, 
        CancellationToken cancellationToken)
    {
        var streamAccumulator = _streamAccumulators[sessionId];
        var responseTypeDetected = false;
        var detectedResponseType = ResponseType.DirectTranslation;
        var currentPhrase = new StringBuilder();
        var isFirstResponse = true;

        try
        {
            // Stream GenAI response (assuming this method exists)
            // Note: This is a placeholder - actual implementation will depend on your GenAI service
            var tokens = SimulateStreamingTokens(systemPrompt + userPrompt);
            
            foreach (var token in tokens)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                streamAccumulator.Append(token);
                currentPhrase.Append(token);

                // Detect response type from first few tokens (only once)
                if (!responseTypeDetected && streamAccumulator.Length > 50)
                {
                    detectedResponseType = DetectResponseType(streamAccumulator.ToString());
                    responseTypeDetected = true;
                    
                    _logger.LogInformation("🎯 Detected response type: {ResponseType} for session {SessionId}", 
                        detectedResponseType, sessionId);

                    // Notify frontend of response type
                    await _notificationService.SendResponseTypeAsync(sessionId, detectedResponseType);
                }

                // Check for complete phrases (sentences or meaningful chunks)
                if (IsCompletePhrase(currentPhrase.ToString()))
                {
                    var phrase = currentPhrase.ToString().Trim();
                    
                    if (!string.IsNullOrEmpty(phrase))
                    {
                        _logger.LogDebug("📝 Complete phrase detected: {Phrase}", phrase);
                        
                        // Process phrase based on response type
                        await ProcessPhraseAsync(sessionId, phrase, detectedResponseType, isFirstResponse, cancellationToken);
                        isFirstResponse = false;
                    }
                    
                    currentPhrase.Clear();
                }

                // Send progressive text update to frontend
                await _notificationService.SendProgressiveTextAsync(sessionId, token);
                
                // Small delay to simulate real streaming
                await Task.Delay(50, cancellationToken);
            }

            // Process any remaining text
            if (currentPhrase.Length > 0)
            {
                var remainingPhrase = currentPhrase.ToString().Trim();
                if (!string.IsNullOrEmpty(remainingPhrase))
                {
                    await ProcessPhraseAsync(sessionId, remainingPhrase, detectedResponseType, false, cancellationToken);
                }
            }

            _logger.LogInformation("✅ Streaming completed for session {SessionId}, total tokens: {TokenCount}", 
                sessionId, streamAccumulator.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during GenAI streaming for session {SessionId}", sessionId);
            throw;
        }
    }

    // Temporary simulation method - replace with actual streaming implementation
    private IEnumerable<string> SimulateStreamingTokens(string fullPrompt)
    {
        var sampleResponse = @"{
  ""response_type"": ""translation"",
  ""translation"": ""Hello, how can I help you today?"",
  ""confidence"": 0.95,
  ""detected_language"": ""en-US"",
  ""facts"": [
    {
      ""type"": ""greeting"",
      ""content"": ""User initiated conversation""
    }
  ]
}";
        
        // Simulate token-by-token streaming
        for (int i = 0; i < sampleResponse.Length; i += 3)
        {
            yield return sampleResponse.Substring(i, Math.Min(3, sampleResponse.Length - i));
        }
    }

    private async Task ProcessPhraseAsync(
        string sessionId, 
        string phrase, 
        ResponseType responseType, 
        bool isFirstPhrase, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🔊 Processing phrase for TTS: {Phrase}", phrase);

            // Determine text to synthesize based on response type
            string textToSynthesize;
            switch (responseType)
            {
                case ResponseType.AIAssistantResponse:
                    textToSynthesize = phrase; // Use AI response directly
                    break;
                case ResponseType.DirectTranslation:
                    textToSynthesize = ExtractTranslationFromPhrase(phrase); // Extract translation portion
                    break;
                default:
                    textToSynthesize = phrase;
                    break;
            }

            if (string.IsNullOrEmpty(textToSynthesize))
            {
                _logger.LogDebug("⏩ Skipping TTS for empty text in session {SessionId}", sessionId);
                return;
            }

            // Start TTS synthesis for this phrase
            var ttsTask = SynthesizePhraseAsync(sessionId, textToSynthesize, isFirstPhrase, cancellationToken);
            
            // Don't await - let TTS run in parallel
            _ = Task.Run(() => ttsTask, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing phrase for session {SessionId}: {Phrase}", sessionId, phrase);
        }
    }

    private async Task SynthesizePhraseAsync(
        string sessionId, 
        string text, 
        bool isFirstPhrase, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🎵 Starting TTS synthesis for phrase: {Text}", text);

            // Simulate TTS streaming - replace with actual implementation
            var audioData = System.Text.Encoding.UTF8.GetBytes($"Audio for: {text}");
            
            var audioChunk = new AudioChunkData
            {
                AudioData = audioData,
                AssociatedText = text,
                IsFirstChunk = isFirstPhrase,
                ChunkIndex = 1,
                TotalChunks = 1
            };

            // Send audio chunk with associated text to frontend
            await _notificationService.SendAudioChunkAsync(sessionId, audioChunk);

            _logger.LogDebug("✅ TTS synthesis completed for phrase in session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during TTS synthesis for session {SessionId}: {Text}", sessionId, text);
        }
    }

    private ResponseType DetectResponseType(string initialTokens)
    {
        var lowercaseTokens = initialTokens.ToLowerInvariant();
        
        // Check for AI assistant response indicators
        if (lowercaseTokens.Contains("\"ai_response\"") || 
            lowercaseTokens.Contains("\"response_type\": \"assistant\"") ||
            lowercaseTokens.Contains("as an ai assistant") ||
            lowercaseTokens.Contains("i need to clarify"))
        {
            return ResponseType.AIAssistantResponse;
        }

        // Check for direct translation indicators
        if (lowercaseTokens.Contains("\"translation\"") || 
            lowercaseTokens.Contains("\"translated_text\""))
        {
            return ResponseType.DirectTranslation;
        }

        // Default to translation
        return ResponseType.DirectTranslation;
    }

    private bool IsCompletePhrase(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 10)
            return false;

        // Check for sentence endings
        var trimmed = text.Trim();
        if (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
            return true;

        // Check for comma-separated clauses (for longer phrases)
        if (trimmed.EndsWith(',') && trimmed.Length > 30)
            return true;

        // Check for natural phrase boundaries in JSON
        if (trimmed.Contains("\",") || trimmed.Contains("\"}"))
            return true;

        return false;
    }

    private string ExtractTranslationFromPhrase(string phrase)
    {
        // Try to extract translation from partial JSON
        try
        {
            // Look for translation field patterns
            if (phrase.Contains("\"translation\":"))
            {
                var startIndex = phrase.IndexOf("\"translation\":") + "\"translation\":".Length;
                var startQuote = phrase.IndexOf('"', startIndex);
                if (startQuote != -1)
                {
                    var endQuote = phrase.IndexOf('"', startQuote + 1);
                    if (endQuote != -1)
                    {
                        return phrase.Substring(startQuote + 1, endQuote - startQuote - 1);
                    }
                }
            }

            // If no clear JSON structure, return the phrase as-is for TTS
            return phrase.Trim('"', '{', '}', ',', ' ');
        }
        catch
        {
            return phrase;
        }
    }

    public async Task<ConversationHistoryUpdate> FinalizeTranslationCycleAsync(string sessionId)
    {
        try
        {
            _logger.LogInformation("🏁 Finalizing translation cycle for session {SessionId}", sessionId);

            StringBuilder? accumulator;
            lock (_operationsLock)
            {
                _streamAccumulators.TryGetValue(sessionId, out accumulator);
                _streamAccumulators.Remove(sessionId);
            }

            if (accumulator == null || accumulator.Length == 0)
            {
                _logger.LogWarning("⚠️ No accumulated content to finalize for session {SessionId}", sessionId);
                return new ConversationHistoryUpdate
                {
                    SessionId = sessionId,
                    IsReadyForNextCycle = true
                };
            }

            var finalResponse = accumulator.ToString();
            
            // Parse the complete JSON response
            var translationResult = ParseFinalTranslationResponse(finalResponse);
            
            // Create conversation turn from notification service types
            var conversationTurn = new ServiceConversationTurn
            {
                TurnId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                OriginalText = translationResult.OriginalText ?? "",
                TranslatedText = translationResult.TranslatedText ?? "",
                ResponseType = ResponseType.DirectTranslation,
                Speaker = new SpeakerInfo() // Placeholder
            };

            // Send final conversation data to frontend
            await _notificationService.SendConversationHistoryAsync(sessionId, new ConversationHistoryData
            {
                ConversationTurn = conversationTurn,
                ExtractedFacts = translationResult.Facts,
                SpeakerAnalysis = new SpeakerInfo(), // Placeholder
                IsTransactionComplete = true
            });

            // Send cycle completion signal
            await _notificationService.SendCycleCompletionAsync(sessionId, true);

            _logger.LogInformation("✅ Translation cycle finalized for session {SessionId}", sessionId);

            return new ConversationHistoryUpdate
            {
                SessionId = sessionId,
                ExtractedFacts = translationResult.Facts,
                IsReadyForNextCycle = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error finalizing translation cycle for session {SessionId}", sessionId);
            return new ConversationHistoryUpdate
            {
                SessionId = sessionId,
                IsReadyForNextCycle = true // Allow next cycle even if finalization failed
            };
        }
    }

    private TranslationResult ParseFinalTranslationResponse(string jsonResponse)
    {
        try
        {
            return JsonSerializer.Deserialize<TranslationResult>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new TranslationResult();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "❌ Failed to parse final JSON response: {Response}", jsonResponse);
            
            // Fallback parsing for partial responses
            return new TranslationResult
            {
                TranslatedText = ExtractTranslationFromPhrase(jsonResponse),
                OriginalText = "Error parsing response",
                Facts = new List<SessionFact>()
            };
        }
    }

    public async Task CancelStreamingAsync(string sessionId)
    {
        lock (_operationsLock)
        {
            if (_activeOperations.TryGetValue(sessionId, out var operation))
            {
                operation.Cancel();
                _activeOperations.Remove(sessionId);
                _streamAccumulators.Remove(sessionId);
                
                _logger.LogInformation("🛑 Cancelled streaming operation for session {SessionId}", sessionId);
            }
        }

        await _notificationService.SendOperationCancelledAsync(sessionId);
    }
}
