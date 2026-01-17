using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Frontend;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.Enums;
using A3ITranslator.Infrastructure.Services.Azure;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using A3ITranslator.Application.Models;
using A3ITranslator.Infrastructure.Services.Translation; // ✅ For ConversationHistoryItem

// ✅ PURE DOMAIN: Type aliases for clean architecture
using DomainSession = A3ITranslator.Application.Domain.Entities.ConversationSession;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

/// <summary>
/// Enhanced conversation orchestrator - Main entry point for audio processing
/// Manages the complete speaker-aware conversation pipeline with SOLID principles
/// Single Responsibility: Orchestrate complete conversation flow from audio to TTS
/// </summary>
public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _logger;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly IStreamingTTSService _ttsService;
    private readonly ISessionRepository _sessionRepository;
    private readonly IStreamingSTTService _sttService;
    private readonly ISpeakerIdentificationService _speakerService;
    private readonly ISpeakerManagementService _speakerManager;
    private readonly ITranslationOrchestrator _translationOrchestrator;
    private readonly IFrontendConversationItemService _frontendService;
    private readonly IAudioFeatureExtractor _featureExtractor;

    // Per-connection conversation state
    private readonly Dictionary<string, ConversationState> _connectionStates = new();
    private readonly object _stateLock = new();

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> logger,
        IRealtimeNotificationService notificationService,
        IStreamingTTSService ttsService,
        ISessionRepository sessionRepository,
        IStreamingSTTService sttService,
        ISpeakerIdentificationService speakerService,
        ISpeakerManagementService speakerManager,
        ITranslationOrchestrator translationOrchestrator,
        IFrontendConversationItemService frontendService,
        IAudioFeatureExtractor featureExtractor)
    {
        _logger = logger;
        _notificationService = notificationService;
        _ttsService = ttsService;
        _sessionRepository = sessionRepository;
        _sttService = sttService;
        _speakerService = speakerService;
        _speakerManager = speakerManager;
        _translationOrchestrator = translationOrchestrator;
        _frontendService = frontendService;
        _featureExtractor = featureExtractor;
    }

    /// <summary>
    /// Main entry point: Process audio chunk with proper state management
    /// Follows the original design: Accept chunks only when Ready, process with VAD timeout
    /// </summary>
    public async Task ProcessAudioChunkAsync(string connectionId, byte[] audioChunk)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        // STRICT DESIGN COMPLIANCE: Only accept audio when Ready or during active reception
        // Once VAD triggers (ProcessingUtterance), reject all new chunks until cycle completes
        if (!state.CanAcceptAudio)
        {
            _logger.LogWarning("🚫 ORCHESTRATOR: Rejecting audio chunk for {ConnectionId} - state: {State}", 
                connectionId, state.CycleState);
            
            // Send signal to frontend to stop sending audio
            await _notificationService.NotifyProcessingStatusAsync(connectionId, 
                $"Processing in progress, please wait... (State: {state.CycleState})");
            return;
        }
        
        // FILTERABLE: Audio chunk received
        Console.WriteLine($"TIMESTAMP_AUDIO_CHUNK: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {audioChunk.Length} bytes");

        // Start NEW cycle only when explicitly Ready (after previous cycle completed)
        if (state.ShouldStartNewCycle)
        {
            // FILTERABLE: Starting completely new conversation cycle
            Console.WriteLine($"TIMESTAMP_NEW_CYCLE_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting new conversation cycle");
            
            state.StartReceivingAudio();
            
            // Write the triggering chunk to start the stream
            var writeResult = state.AudioStreamChannel.Writer.TryWrite(audioChunk);
            if (!writeResult)
            {
                _logger.LogWarning("❌ ORCHESTRATOR: Failed to write initial audio chunk to channel for {ConnectionId}", connectionId);
                return;
            }
            
            _ = Task.Run(() => ProcessConversationPipelineAsync(connectionId, state));
            
            _logger.LogDebug("🎤 ORCHESTRATOR: Started new conversation cycle for {ConnectionId}", connectionId);
        }
        else if (state.CycleState == ConversationPhase.ReceivingAudio)
        {
            // Continue feeding existing pipeline with additional chunks
            var writeResult = state.AudioStreamChannel.Writer.TryWrite(audioChunk);
            if (!writeResult)
            {
                _logger.LogWarning("❌ ORCHESTRATOR: Failed to write audio chunk to active pipeline for {ConnectionId}", connectionId);
            }
        }
    }

    /// <summary>
    /// Initialize connection pipeline with language candidates
    /// Prepares the conversation state for incoming audio processing
    /// </summary>
    public async Task InitializeConnectionPipeline(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🚀 ORCHESTRATOR: Initializing pipeline for {ConnectionId} with languages: [{Languages}]",
                connectionId, string.Join(", ", candidateLanguages));

            // Get or create conversation state
            var state = GetOrCreateConversationState(connectionId);

            // Load session to get configuration
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, cancellationToken);
            if (session == null)
            {
                _logger.LogWarning("⚠️ ORCHESTRATOR: Session not found for {ConnectionId}, cannot initialize pipeline", connectionId);
                return;
            }

            // Cache session configuration in the conversation state
            state.CacheSessionConfig(session.SessionId, session.PrimaryLanguage, session.SecondaryLanguage ?? "en-US");

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ORCHESTRATOR: Failed to initialize pipeline for {ConnectionId}", connectionId);
            throw;
        }
    }

/// <summary>
/// Enhanced conversation pipeline with speaker-aware processing
/// </summary>
    private async Task ProcessConversationPipelineAsync(string connectionId, ConversationState state)
    {
        // 🎫 CYCLE TOKEN: Cancelled when VAD detects silence or STT finishes
        using var cycleCts = new CancellationTokenSource();
        
        try
        {
            // 🔄 CYCLE START: Fetch session config once per cycle
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogError("❌ Session not found for {ConnectionId}", connectionId);
                return;
            }

            // Cache session config for this cycle
            state.CacheSessionConfig(session.SessionId, session.PrimaryLanguage, session.SecondaryLanguage ?? "en-US");
            
            _logger.LogDebug("🔄 CYCLE START: Processing conversation cycle for session {SessionId}", session.SessionId);

            var utteranceCollector = new MultiLanguageSpeakerAwareUtteranceCollector();

            // 1. Create fan-out channels (temporary for this cycle)
            // 1. Create fan-out channels (temporary for this cycle)
            var sttChannel = Channel.CreateUnbounded<byte[]>();
            
            // ✨ NEW: Background Rolling Feature Accumulator (Zero Latency)
            var rollingFingerprint = new AudioFingerprint();

            // 2. Start parallel processing tasks
            // CRITICAL: Start broadcaster FIRST so audio flows to channels before consumers start
            Console.WriteLine($"TASK_START_BroadcastAudioAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting broadcaster task");
            var broadcasterTask = BroadcastAudioAsync(
                state.AudioStreamChannel.Reader, 
                sttChannel, 
                rollingFingerprint, // Pass accumulator
                connectionId, 
                cycleCts.Token);
            
            Console.WriteLine($"TASK_START_ProcessSTTWithSpeakerContextAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting STT task");
            var sttTask = ProcessSTTWithSpeakerContextAsync(sttChannel.Reader, utteranceCollector, state.CandidateLanguages, connectionId, cycleCts.Token);

            _logger.LogDebug("📡 ORCHESTRATOR: Started broadcaster and consumer tasks for {ConnectionId}", connectionId);

            // 3. VAD MONITOR: Polling task to check for silence after speech
            Console.WriteLine($"TASK_START_MonitorTask: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting VAD monitor task");
            var monitorTask = Task.Run(async () => 
            {
                while (!cycleCts.Token.IsCancellationRequested)
                {
                    // If we have text and have timed out (enough silence), stop the cycle
                    if (utteranceCollector.HasTimedOut() && utteranceCollector.HasAccumulatedText)
                    {
                        Console.WriteLine($"TIMESTAMP_VAD_TIMEOUT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - VAD triggered");
                        _logger.LogInformation("🔇 VAD: Silence detected for {ConnectionId}, ending cycle", connectionId);
                        cycleCts.Cancel();
                        break;
                    }
                    await Task.Delay(250, cycleCts.Token);
                }
                Console.WriteLine($"TASK_END_MonitorTask: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - VAD monitor task completed");
            }, cycleCts.Token);

            // 4. Wait for ANY task to signal completion (VAD, STT end, or Error)
            // CRITICAL: Include broadcasterTask so audio actually flows to STT channels
            Console.WriteLine($"TASK_WAIT_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting for any task completion");
            await Task.WhenAny(broadcasterTask, sttTask, monitorTask);
            Console.WriteLine($"TASK_WAIT_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - One task completed, cancelling others");

            // Cancel everything else if one finished naturally
            if (!cycleCts.IsCancellationRequested) cycleCts.Cancel();

            // 5. VAD detected silence or STT finished - transition to processing phase
            if (utteranceCollector.HasAccumulatedText)
            {
                // 🔄 PHASE TRANSITION: ReceivingAudio → ProcessingUtterance
                state.StartProcessing();
                _logger.LogInformation("🔄 PHASE TRANSITION: {ConnectionId} entered ProcessingUtterance phase", connectionId);
                
                // Send signal to frontend to stop sending audio
                await _notificationService.NotifyProcessingStatusAsync(connectionId, 
                    "Processing your message, please wait...");

                Console.WriteLine($"TIMESTAMP_PROCESS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting ProcessCompletedUtteranceAsync");
                Console.WriteLine($"TIMESTAMP_PROCESS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting ProcessCompletedUtteranceAsync");
                
                // Pass the accumulated fingerprint to the processing logic
                utteranceCollector.SetSpeakerContext(null, 0, rollingFingerprint);
                
                await ProcessCompletedUtteranceAsync(connectionId, utteranceCollector, state);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("🎬 Pipeline task cancelled for {ConnectionId} (End of cycle)", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in conversation pipeline for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Processing error occurred");
        }
        finally
        {
            // 🏁 CYCLE END: Reset for next cycle
            state.ResetCycle();
            _logger.LogDebug("🔄 CYCLE END: Ready for next conversation cycle on {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Broadcast audio to multiple processing channels
    /// </summary>
    private async Task BroadcastAudioAsync(
        ChannelReader<byte[]> audioReader, 
        Channel<byte[]> sttChannel, 
        AudioFingerprint fingerprintAccumulator,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var chunkCount = 0;
        try
        {
            Console.WriteLine($"TASK_START_BroadcastAudioAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Broadcaster starting audio processing");
            
            // Read until channel is closed OR until this cycle is cancelled (VAD detected)
            while (await audioReader.WaitToReadAsync(cancellationToken))
            {
                while (audioReader.TryRead(out var chunk))
                {
                    chunkCount++;
                    await sttChannel.Writer.WriteAsync(chunk, cancellationToken);
                    
                    // ✨ Background Feature Extraction (Zero Latency)
                    _ = _featureExtractor.AccumulateFeaturesAsync(chunk, fingerprintAccumulator);
                }
            }
            Console.WriteLine($"TASK_END_BroadcastAudioAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Broadcaster completed normally");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"TASK_CANCELLED_BroadcastAudioAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Broadcaster cancelled after {chunkCount} chunks");
            _logger.LogDebug("🎬 BROADCASTER: Cycle cancelled after {ChunkCount} chunks for {ConnectionId}", chunkCount, connectionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TASK_ERROR_BroadcastAudioAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Broadcaster error after {chunkCount} chunks: {ex.Message}");
            _logger.LogError(ex, "❌ BROADCASTER: Error after {ChunkCount} chunks for {ConnectionId}", chunkCount, connectionId);
        }
        finally
        {
            sttChannel.Writer.TryComplete();
            Console.WriteLine($"TASK_FINALLY_BroadcastAudioAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Broadcaster cleanup completed");
        }
    }

    /// <summary>
    /// Process STT with speaker context and language resolution
    /// </summary>
    private async Task ProcessSTTWithSpeakerContextAsync(
        ChannelReader<byte[]> audioReader,
        MultiLanguageSpeakerAwareUtteranceCollector utteranceCollector,
        string[] candidateLanguages,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"TASK_START_ProcessSTTWithSpeakerContextAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT processing starting");
            
            await foreach (var result in _sttService.ProcessAutoLanguageDetectionAsync(audioReader, candidateLanguages, cancellationToken))
            {
                Console.WriteLine($"TIMESTAMP_STT_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Text: '{result.Text}' - IsFinal: {result.IsFinal}");
                utteranceCollector.AddResult(result);
                
                // Send live transcription updates to frontend
                var displayText = utteranceCollector.GetCurrentDisplayText();
                Console.WriteLine($"TIMESTAMP_FRONTEND_TRANSCRIPTION_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending to frontend: '{displayText}'");
                await _notificationService.NotifyTranscriptionAsync(connectionId, displayText, result.Language, false);
            }
        }
        catch (OperationCanceledException) 
        { 
            Console.WriteLine($"TASK_CANCELLED_ProcessSTTWithSpeakerContextAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT task cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TASK_ERROR_ProcessSTTWithSpeakerContextAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT task error: {ex.Message}");
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(ex, "❌ STT processing error for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Process speaker identification with "First-Capture-Locking" for zero latency
    /// </summary>

    private async Task ProcessCompletedUtteranceAsync(
        string connectionId, 
        MultiLanguageSpeakerAwareUtteranceCollector utteranceCollector,
        ConversationState state)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_UTTERANCE_PROCESSING_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting utterance processing");

            var utteranceWithContext = utteranceCollector.GetUtteranceWithResolvedLanguages(
                state.CandidateLanguages,
                state.PrimaryLanguage!);

            Console.WriteLine($"TIMESTAMP_UTTERANCE_RESOLVED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Utterance languages resolved: {utteranceWithContext.SourceLanguage} → {utteranceWithContext.TargetLanguage}");

            Console.WriteLine($"TIMESTAMP_GENAI_REQUEST_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending to Translation Orchestrator");
            var genAIResponse = await ProcessWithGenAI(utteranceWithContext, state);
            Console.WriteLine($"TIMESTAMP_GENAI_RESPONSE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Translation Orchestrator completed");

            // 🚀 CLEAN: One speaker manager to rule them all
            var speakerPayload = new Application.DTOs.Translation.SpeakerServicePayload
            {
                Identification = genAIResponse.SpeakerIdentification ?? new(),
                ProfileUpdate = genAIResponse.SpeakerProfileUpdate ?? new(),
                AudioLanguage = utteranceWithContext.DominantLanguage,
                TranscriptionConfidence = utteranceWithContext.TranscriptionConfidence,
                AudioFingerprint = utteranceWithContext.AudioFingerprint // Pass raw DNA for sync
            };

            Console.WriteLine($"TIMESTAMP_SPEAKER_PROCESS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Processing speaker identification");
            // Pass the GenAI Decision (Identification) + Raw DNA (Payload) to the Manager
            var speakerResult = await _speakerManager.ProcessSpeakerIdentificationAsync(state.SessionId!, speakerPayload);
            
            state.LastSpeakerId = speakerResult.SpeakerId;

            Console.WriteLine($"TIMESTAMP_PARALLEL_RESPONSES_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting parallel response processing");
            state.StartCompleting();
            
            // 🚀 PARALLEL PROCESSING: Send all responses concurrently
            await SendConversationResponseParallelAsync(connectionId, utteranceWithContext, genAIResponse, speakerResult);
            
            Console.WriteLine($"TIMESTAMP_PARALLEL_RESPONSES_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Parallel response processing completed");
            
            // FILTERABLE: Complete utterance signal sent
            Console.WriteLine($"TIMESTAMP_CYCLE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Cycle completion signal sent");
            await _notificationService.NotifyCycleCompletionAsync(connectionId, false);
            
            utteranceCollector.Reset();
            Console.WriteLine($"TIMESTAMP_UTTERANCE_PROCESSING_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Utterance processing completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TIMESTAMP_UTTERANCE_PROCESSING_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Error: {ex.Message}");
            _logger.LogError(ex, "❌ Error finalising utterance for {ConnectionId}", connectionId);
        }
    }

    private async Task<A3ITranslator.Application.DTOs.Translation.EnhancedTranslationResponse> ProcessWithGenAI(UtteranceWithContext utterance, ConversationState state)
    {
        // 🚀 FETCH HISTORY: Get last 5 turns
        var session = await _sessionRepository.GetByIdAsync(state.SessionId!, CancellationToken.None);
        var recentHistory = new List<ConversationHistoryItem>();
        if (session != null)
        {
             recentHistory = session.ConversationHistory
                .TakeLast(5)
                .Select(t => new ConversationHistoryItem 
                { 
                    SpeakerId = t.SpeakerId, 
                    SpeakerName = t.SpeakerName, 
                    Text = t.OriginalText 
                })
                .ToList();
        }

        var request = new A3ITranslator.Application.DTOs.Translation.EnhancedTranslationRequest
        {
            Text = utterance.Text,
            SourceLanguage = utterance.SourceLanguage,
            TargetLanguage = utterance.TargetLanguage,
            SessionContext = new Dictionary<string, object>
            {
                ["sessionId"] = state.SessionId!,
                ["speakers"] = _speakerManager.GetSessionSpeakers(state.SessionId!)
                                .Select(s => new { s.SpeakerId, s.DisplayName, s.Insights.AssignedRole }),
                ["lastSpeaker"] = state.LastSpeakerId ?? "None",
                ["audioProvisionalId"] = utterance.ProvisionalSpeakerId ?? "Unknown",
                ["recentHistory"] = recentHistory
            }
        };

        // 🚀 FEATURES-ONLY FLOW: Inject Comparison Scorecard
        if (utterance.AudioFingerprint != null)
        {
            var candidates = _speakerManager.GetSessionSpeakers(state.SessionId!);
            var scorecard = _speakerService.CompareFingerprints(utterance.AudioFingerprint, candidates);
            request.SessionContext["speakerScorecard"] = scorecard;
            request.SessionContext["acousticDNA"] = utterance.AudioFingerprint; // Send raw if needed by prompt builder
        }

        // Use the enhanced translation processing method
        return await _translationOrchestrator.ProcessEnhancedTranslationAsync(request);
    }

    private async Task SendConversationResponseParallelAsync(
        string connectionId, 
        UtteranceWithContext utterance, 
        A3ITranslator.Application.DTOs.Translation.EnhancedTranslationResponse translationResponse, 
        SpeakerOperationResult speakerUpdate)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_PARALLEL_CONVERSATION_RESPONSE_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting parallel conversation response processing");

            var state = GetOrCreateConversationState(connectionId);

            // Prepare all the data needed for parallel operations
            var speakers = _speakerManager.GetSessionSpeakers(state.SessionId ?? "");
            var activeSpeaker = speakers.FirstOrDefault(s => s.SpeakerId == (speakerUpdate.SpeakerId ?? state.LastSpeakerId)) 
                ?? new SpeakerProfile { SpeakerId = "unknown", DisplayName = "Unknown Speaker" };

            // 🚀 STEP 1: Process and Send Speaker Update Sequentially (Ensures UI has name before bubble)
            if (speakerUpdate.Success)
            {
                Console.WriteLine($"TIMESTAMP_SPEAKER_SEQUENTIAL_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting speaker profile logic");
                var frontendSpeakerUpdate = _frontendService.CreateSpeakerListUpdate(speakers);
                await _notificationService.SendFrontendSpeakerListAsync(connectionId, frontendSpeakerUpdate);
                
                _logger.LogInformation("🎭 Speaker profile updated and sent: {SpeakerId} ({DisplayName})", 
                    speakerUpdate.SpeakerId, activeSpeaker.DisplayName);
            }

            // 🚀 STEP 2: Send other responses in parallel
            var tasks = new List<Task>();
            
            // Determine TTS text and language - prioritize AI response when available
            string ttsText;
            string ttsLanguage;
            
            if (translationResponse.AIAssistance.TriggerDetected && 
                !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
            {
                // Use AI response for TTS when AI assistance is triggered
                ttsText = translationResponse.AIAssistance.Response??string.Empty;
                ttsLanguage = translationResponse.AIAssistance.ResponseLanguage ?? translationResponse.TranslationLanguage ?? "en";
            }
            else
            {
                // Use regular translation for TTS
                ttsText = translationResponse.Translation ?? "";
                ttsLanguage = translationResponse.TranslationLanguage ?? "en";
            }

            if (!string.IsNullOrEmpty(ttsText))
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await SendToTTSContinuousAsync(connectionId, ttsText, ttsLanguage, state);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Continuous TTS error for {ConnectionId}", connectionId);
                    }
                }));
            }

            // Re-create the conversation items with the UPDATED active speaker info
            var mainConversationItem = _frontendService.CreateFromTranslation(
                utterance,
                translationResponse.Translation ?? "",
                translationResponse.TranslationLanguage ?? "en",
                translationResponse.Confidence,
                activeSpeaker,
                utterance.SpeakerConfidence
            );

            FrontendConversationItem? aiConversationItem = null;
            if (translationResponse.AIAssistance.TriggerDetected && 
                !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
            {
                aiConversationItem = _frontendService.CreateFromAIResponse(
                    utterance.DominantLanguage,
                    translationResponse.AIAssistance.Response??string.Empty,
                    translationResponse.AIAssistance.ResponseTranslated,
                    translationResponse.AIAssistance.ResponseLanguage ?? translationResponse.TranslationLanguage ?? "en",
                    translationResponse.Confidence
                );
            }

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _notificationService.SendFrontendConversationItemAsync(connectionId, mainConversationItem);
                    if (aiConversationItem != null)
                    {
                        await _notificationService.SendFrontendConversationItemAsync(connectionId, aiConversationItem);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Conversation item error for {ConnectionId}", connectionId);
                }
            }));

            // Wait for all parallel tasks to complete
            await Task.WhenAll(tasks);
            Console.WriteLine($"TIMESTAMP_PARALLEL_CONVERSATION_RESPONSE_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Parallel conversation response processing completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TIMESTAMP_PARALLEL_CONVERSATION_RESPONSE_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Error: {ex.Message}");
            _logger.LogError(ex, "❌ Error in parallel conversation response for {ConnectionId}", connectionId);
        }
    }

    private ConversationState GetOrCreateConversationState(string connectionId)
    {
        lock (_stateLock)
        {
            if (!_connectionStates.TryGetValue(connectionId, out var state))
            {
                state = new ConversationState(connectionId);
                _connectionStates[connectionId] = state;
            }
            return state;
        }
    }

    /// <summary>
    /// Enhanced TTS processing with gender-aware voice selection and cost optimization
    /// </summary>
    private async Task SendToTTSAsync(string connectionId, string text, string language, ConversationState state)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_TTS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting TTS processing");

            var speakerGender = SpeakerGender.Unknown;
            
            // Try to get gender from last identified speaker for optimal voice selection
            if (state?.SessionId != null && !string.IsNullOrEmpty(state.LastSpeakerId))
            {
                Console.WriteLine($"TIMESTAMP_TTS_SPEAKER_LOOKUP: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Looking up speaker gender");
                var sessionSpeakers = _speakerManager.GetSessionSpeakers(state.SessionId);
                var lastSpeaker = sessionSpeakers?.FirstOrDefault(s => s.SpeakerId == state.LastSpeakerId);
                if (lastSpeaker != null)
                {
                    speakerGender = lastSpeaker.Gender; // Use directly, same enum
                    _logger.LogInformation("🎭 Using detected speaker gender {Gender} for TTS voice selection", speakerGender);
                }
            }

            // Use neural voice service with gender-aware selection if available
            if (_ttsService is AzureNeuralVoiceService neuralVoiceService)
            {
                Console.WriteLine($"TIMESTAMP_TTS_NEURAL_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Using Neural TTS service");
                _logger.LogInformation("🔊 Sending to Neural TTS: {Text} (Language: {Language}, Gender: {Gender})", 
                    text, language, speakerGender);
                
                var chunkCount = 0;
                // Use gender-aware voice synthesis with standard voices by default for cost optimization
                await foreach (var chunk in neuralVoiceService.SynthesizeWithGenderAsync(
                    text, language, speakerGender, VoiceStyle.Conversational, isPremium: false))
                {
                    chunkCount++;
                    Console.WriteLine($"TIMESTAMP_TTS_CHUNK_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending TTS chunk {chunkCount}: {chunk.AudioData.Length} bytes");
                    
                    // Create and send frontend TTS chunk
                    var frontendTTSChunk = _frontendService.CreateTTSChunk(
                        "neural-tts-" + Guid.NewGuid().ToString()[..8], // conversationItemId
                        Convert.ToBase64String(chunk.AudioData),        // audioData as base64
                        chunk.AssociatedText,                          // text
                        chunk.ChunkIndex,                              // chunkIndex
                        chunk.TotalChunks,                             // totalChunks
                        0.0,                                           // durationMs (could be calculated if needed)
                        "audio/mp3"                                    // audioFormat
                    );
                    
                    await _notificationService.SendFrontendTTSChunkAsync(connectionId, frontendTTSChunk);
                    
                    Console.WriteLine($"TIMESTAMP_TTS_CHUNK_SENT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - TTS chunk {chunkCount} sent successfully");
                    _logger.LogTrace("🎵 Neural TTS chunk sent: {Size} bytes", chunk.AudioData.Length);
                }
                
                Console.WriteLine($"TIMESTAMP_TTS_NEURAL_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Neural TTS completed, total chunks: {chunkCount}");
            }
            else
            {
                Console.WriteLine($"TIMESTAMP_TTS_STANDARD_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Using Standard TTS service");
                // Fallback to standard TTS service
                _logger.LogInformation("🔊 Sending to Standard TTS: {Text} (Language: {Language})", text, language);
                await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language);
                Console.WriteLine($"TIMESTAMP_TTS_STANDARD_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Standard TTS completed");
            }

            Console.WriteLine($"TIMESTAMP_TTS_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - TTS processing completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TIMESTAMP_TTS_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - TTS error: {ex.Message}");
            _logger.LogError(ex, "❌ Error sending text to enhanced TTS for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Continuous TTS processing that sends chunks independently until completion
    /// This method runs independently and doesn't wait for other processing to complete
    /// </summary>
    private async Task SendToTTSContinuousAsync(string connectionId, string text, string language, ConversationState state)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting continuous TTS processing");

            var speakerGender = SpeakerGender.Unknown;
            
            // Try to get gender from last identified speaker for optimal voice selection
            if (state?.SessionId != null && !string.IsNullOrEmpty(state.LastSpeakerId))
            {
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_SPEAKER_LOOKUP: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Looking up speaker gender for continuous TTS");
                var sessionSpeakers = _speakerManager.GetSessionSpeakers(state.SessionId);
                var lastSpeaker = sessionSpeakers?.FirstOrDefault(s => s.SpeakerId == state.LastSpeakerId);
                if (lastSpeaker != null)
                {
                    speakerGender = lastSpeaker.Gender;
                    _logger.LogInformation("🎭 Continuous TTS using detected speaker gender {Gender}", speakerGender);
                }
            }

            // Use neural voice service with gender-aware selection if available
            if (_ttsService is AzureNeuralVoiceService neuralVoiceService)
            {
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_NEURAL_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Using Neural TTS for continuous streaming");
                _logger.LogInformation("🔊 Starting continuous Neural TTS: {Text} (Language: {Language}, Gender: {Gender})", 
                    text, language, speakerGender);
                
                var chunkCount = 0;
                var conversationItemId = "continuous-tts-" + Guid.NewGuid().ToString()[..8];
                
                // 🚀 CONTINUOUS CHUNK SENDING: Each chunk is sent immediately as it's generated
                await foreach (var chunk in neuralVoiceService.SynthesizeWithGenderAsync(
                    text, language, speakerGender, VoiceStyle.Conversational, isPremium: false))
                {
                    chunkCount++;
                    Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_CHUNK_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending continuous TTS chunk {chunkCount}: {chunk.AudioData.Length} bytes");
                    
                    // Create and send frontend TTS chunk immediately
                    var frontendTTSChunk = _frontendService.CreateTTSChunk(
                        conversationItemId,                             // conversationItemId (consistent for all chunks)
                        Convert.ToBase64String(chunk.AudioData),        // audioData as base64
                        chunk.AssociatedText,                          // text
                        chunk.ChunkIndex,                              // chunkIndex
                        chunk.TotalChunks,                             // totalChunks
                        0.0,                                           // durationMs (could be calculated if needed)
                        "audio/mp3"                                    // audioFormat
                    );
                    
                    // 🚀 IMMEDIATE SENDING: No waiting for other processes
                    await _notificationService.SendFrontendTTSChunkAsync(connectionId, frontendTTSChunk);
                    
                    Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_CHUNK_SENT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous TTS chunk {chunkCount} sent successfully");
                    _logger.LogTrace("🎵 Continuous TTS chunk sent: {Size} bytes", chunk.AudioData.Length);
                }
                
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_NEURAL_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous Neural TTS completed, total chunks: {chunkCount}");
            }
            else
            {
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_STANDARD_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Using Standard TTS for continuous streaming");
                // Fallback to standard TTS service with continuous streaming
                _logger.LogInformation("🔊 Starting continuous Standard TTS: {Text} (Language: {Language})", text, language);
                
                // Use the standard TTS service's continuous streaming
                await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language);
                
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_STANDARD_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous Standard TTS completed");
            }

            Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous TTS processing completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous TTS error: {ex.Message}");
            _logger.LogError(ex, "❌ Error in continuous TTS processing for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Parse speaker gender string to SpeakerGender enum
    /// </summary>
    private static SpeakerGender ParseSpeakerGender(string? genderString)
    {
        if (string.IsNullOrEmpty(genderString)) return SpeakerGender.Unknown;
        
        return genderString.ToLowerInvariant() switch
        {
            "male" or "m" => SpeakerGender.Male,
            "female" or "f" => SpeakerGender.Female,
            "nonbinary" or "nb" or "non-binary" => SpeakerGender.NonBinary,
            _ => SpeakerGender.Unknown
        };
    }

    public async Task CleanupConnection(string connectionId)
    {
        lock (_stateLock)
        {
            if (_connectionStates.Remove(connectionId, out var state))
            {
                state.Dispose();
                _logger.LogInformation("🧹 Cleaned up conversation state for {ConnectionId}", connectionId);
            }
        }
        
        var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
        await _sessionRepository.RemoveByConnectionIdAsync(connectionId, CancellationToken.None);

        if (session != null)
            await _speakerManager.ClearSessionAsync(session.SessionId);
    }

    private async Task SaveConversationItemAsync(string sessionId, UtteranceWithContext utterance, dynamic genAIResponse) => await Task.CompletedTask;

    // --- Legacy / Infrastructure Interfaces ---
    public Task<ConversationResult> ProcessTranscriptionAsync(string c, string t, string d, float f, CancellationToken ct = default) => Task.FromResult(new ConversationResult { Success = true });
    public Task<bool> ProcessGeneratedResponseAsync(string c, ConversationItem i, CancellationToken ct = default) => Task.FromResult(true);
    public Task CompleteConversationCycleAsync(string c, string i, CancellationToken ct = default) => Task.CompletedTask;
    public Task HandleConversationErrorAsync(string c, string e, ConversationErrorSeverity s, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ProcessTTSResponseAsync(string c, ConversationItem i, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> NotifyCycleCompletionAsync(string c, CancellationToken ct = default) { _notificationService.NotifyCycleCompletionAsync(c, false); return Task.FromResult(true); }
}

public class ConversationState : IDisposable
{
    public string ConnectionId { get; }
    public Channel<byte[]> AudioStreamChannel { get; } = Channel.CreateUnbounded<byte[]>();
    public ConversationPhase CycleState { get; set; } = ConversationPhase.Ready;
    public string? SessionId { get; private set; }
    public string? PrimaryLanguage { get; private set; }
    public string? SecondaryLanguage { get; private set; }
    public string? LastSpeakerId { get; set; } // For Room Memory
    
    public string[] CandidateLanguages => new[] { PrimaryLanguage ?? "en-US", SecondaryLanguage ?? "en-GB" };
    
    // STRICT DESIGN: Only accept audio when Ready (new cycle) or actively receiving (before VAD timeout)
    // Once VAD triggers ProcessingUtterance/SendingResponse, reject ALL new chunks until cycle completes
    public bool CanAcceptAudio => CycleState == ConversationPhase.Ready || CycleState == ConversationPhase.ReceivingAudio;
    
    public bool ShouldStartNewCycle => CycleState == ConversationPhase.Ready;
    
    public ConversationState(string connectionId) => ConnectionId = connectionId;

    public void CacheSessionConfig(string sessionId, string primary, string secondary)
    {
        SessionId = sessionId;
        PrimaryLanguage = primary;
        SecondaryLanguage = secondary;
    }

    public void StartReceivingAudio() => CycleState = ConversationPhase.ReceivingAudio;
    public void StartProcessing() => CycleState = ConversationPhase.ProcessingUtterance;
    public void StartCompleting() => CycleState = ConversationPhase.SendingResponse;
    public void ResetCycle() => CycleState = ConversationPhase.Ready;
    
    public void Dispose() => AudioStreamChannel.Writer.TryComplete();
}
