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
using DomainConversationTurn = A3ITranslator.Application.Domain.Entities.ConversationTurn;
using ModelConversationTurn = A3ITranslator.Application.Models.ConversationTurn;

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
    /// Signal utterance completion from frontend VAD
    /// Triggers processing of the accumulated utterance through GenAI and TTS
    /// </summary>
    public async Task CompleteUtteranceAsync(string connectionId)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        _logger.LogInformation("🔇 Frontend VAD: Utterance completion signal received for {ConnectionId}", connectionId);
        
        // ⚡ SMART APPROACH: Signal utterance completion first
        Console.WriteLine($"TIMESTAMP_UTTERANCE_COMPLETION_SIGNAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Marking utterance as completed");
        state.CompleteUtterance();
        
        // ⚡ SURGICAL CANCELLATION: Cancel only STT processing, leave audio stream intact
        Console.WriteLine($"TIMESTAMP_STT_CANCELLATION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Cancelling STT processing for immediate completion");
        state.CancelCurrentCycle(); // This will stop STT processing gracefully
        
        // ⚡ IMMEDIATE PROCESSING: Start processing immediately with accumulated transcription
        if (state.HasAccumulatedText && !state.IsProcessingStarted)
        {
            state.IsProcessingStarted = true; // Prevent double processing
            Console.WriteLine($"TIMESTAMP_IMMEDIATE_PROCESSING_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting immediate utterance processing");
            var rollingFingerprint = new AudioFingerprint(); // Create minimal fingerprint
            await ProcessUtteranceWithTransition(connectionId, state, "Frontend VAD (Immediate)", rollingFingerprint);
        }
        
        _logger.LogDebug("✅ Utterance processed immediately for {ConnectionId}", connectionId);
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
        
        // ⚡ IMMEDIATE CANCELLATION: Store token source for instant cancellation from CompleteUtterance
        state.CurrentCycleCts = cycleCts;
        
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

            // ✅ SIMPLIFIED: No separate utterance collector needed - state handles everything

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
            var sttTask = ProcessSTTWithSpeakerContextAsync(sttChannel.Reader, state, connectionId, cycleCts.Token);

            _logger.LogDebug("📡 ORCHESTRATOR: Started broadcaster and consumer tasks for {ConnectionId}", connectionId);

            // 3. ⚡ SMART WAITING: Wait for STT completion OR utterance completion (don't wait for broadcaster)
            Console.WriteLine($"TASK_WAIT_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting for STT completion or utterance completion");
            
            // Create a completion task that triggers when utterance is marked complete
            var utteranceCompletionTask = Task.Run(async () =>
            {
                while (!cycleCts.Token.IsCancellationRequested && !state.IsUtteranceCompleted)
                {
                    await Task.Delay(50, cycleCts.Token); // Fast polling for utterance completion
                }
            });
            
            // Wait for EITHER STT task completion OR utterance completion (NOT broadcaster)
            await Task.WhenAny(sttTask, utteranceCompletionTask);
            Console.WriteLine($"TASK_WAIT_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT task or utterance completed");

            // Cancel everything immediately
            if (!cycleCts.IsCancellationRequested) cycleCts.Cancel();
            
  

            // 5. ⚡ PROCESSING OPTIMIZATION: Process immediately if utterance is complete
            if (state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                // 🎯 FRONTEND VAD: Frontend explicitly signaled completion
                _logger.LogInformation("🎯 Frontend VAD completion detected - processing utterance immediately for {ConnectionId}", connectionId);
                await ProcessUtteranceWithTransition(connectionId, state, "Frontend VAD Signal", rollingFingerprint);
            }
            else if (!state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                // 🔄 STT CHANNEL CLOSED: We have text but no explicit frontend signal - still process it
                // This happens when STT completes naturally or client disconnects
                _logger.LogInformation("🔄 STT channel closed with text but no frontend signal - auto-completing utterance for {ConnectionId}", connectionId);
                state.CompleteUtterance(); // Mark as completed
                await ProcessUtteranceWithTransition(connectionId, state, "Auto-completion (STT ended)", rollingFingerprint);
            }
            else if (state.IsProcessingStarted)
            {
                _logger.LogDebug("✅ Utterance already being processed by CompleteUtteranceAsync for {ConnectionId}", connectionId);
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
            // 🏁 CYCLE END: Reset for next cycle (utterance state reset handled in ResetCycle)
            state.ResetCycle();
            _logger.LogDebug("🔄 CYCLE END: Ready for next conversation cycle on {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Helper method to handle utterance processing with phase transitions
    /// </summary>
    private async Task ProcessUtteranceWithTransition(string connectionId, ConversationState state, string trigger, AudioFingerprint rollingFingerprint)
    {
        // 🔄 PHASE TRANSITION: ReceivingAudio → ProcessingUtterance
        state.StartProcessing();
        _logger.LogInformation("🔄 PHASE TRANSITION: {ConnectionId} entered ProcessingUtterance phase via {Trigger}", connectionId, trigger);
        
        Console.WriteLine($"TIMESTAMP_PROCESS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting ProcessCompletedUtteranceAsync via {trigger}");
        
        // Pass the accumulated fingerprint to the processing logic
        state.SetSpeakerContext(null, 0, rollingFingerprint);
        
        await ProcessCompletedUtteranceAsync(connectionId, state);
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
        ConversationState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"TASK_START_ProcessSTTWithSpeakerContextAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT processing starting");
            
            await foreach (var result in _sttService.ProcessAutoLanguageDetectionAsync(audioReader, state.CandidateLanguages, cancellationToken))
            {
                // ⚡ SMART FILTER: Stop processing STT results after utterance completion
                if (state.IsUtteranceCompleted)
                {
                    Console.WriteLine($"TIMESTAMP_STT_RESULT_IGNORED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Ignoring STT result after utterance completion: '{result.Text}'");
                    break; // Stop processing new STT results gracefully
                }
                
                Console.WriteLine($"TIMESTAMP_STT_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Text: '{result.Text}' - IsFinal: {result.IsFinal}");
                state.AddTranscriptionResult(result);
                
                // Send live transcription updates to frontend
                var displayText = state.GetCurrentDisplayText();
                Console.WriteLine($"TIMESTAMP_FRONTEND_TRANSCRIPTION_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending to frontend: '{displayText}'");
                await _notificationService.NotifyTranscriptionAsync(connectionId, displayText, result.Language, false);
            }
        }
        catch (OperationCanceledException) 
        { 
            Console.WriteLine($"TASK_CANCELLED_ProcessSTTWithSpeakerContextAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT task cancelled (expected on utterance completion)");
            _logger.LogDebug("🔇 STT processing cancelled for {ConnectionId} (expected behavior)", connectionId);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Aborted && ex.Status.Detail.Contains("Stream timed out"))
        {
            Console.WriteLine($"TASK_TIMEOUT_ProcessSTTWithSpeakerContextAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Google STT timeout (expected when utterance completed early)");
            _logger.LogDebug("⏰ Google STT timeout for {ConnectionId} - this is expected when utterance completion happens before natural STT end", connectionId);
            // This is expected behavior when we call CompleteUtterance() - not an error
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
        ConversationState state)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_UTTERANCE_PROCESSING_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting utterance processing");

            var utteranceWithContext = state.GetCompleteUtterance();

            Console.WriteLine($"TIMESTAMP_UTTERANCE_RESOLVED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Utterance languages resolved: {utteranceWithContext.SourceLanguage} → {utteranceWithContext.TargetLanguage}");

            Console.WriteLine($"TIMESTAMP_GENAI_REQUEST_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending to Translation Orchestrator");
            var genAIResponse = await ProcessWithGenAI(utteranceWithContext, state);
            Console.WriteLine($"TIMESTAMP_GENAI_RESPONSE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Translation Orchestrator completed");

            // 🚀 CLEAN: One speaker manager to rule them all
            var speakerPayload = new Application.DTOs.Translation.SpeakerServicePayload
            {
                Identification = genAIResponse.SpeakerIdentification ?? new(),
                ProfileUpdate = genAIResponse.SpeakerProfileUpdate ?? new(),
                AudioLanguage = genAIResponse.AudioLanguage, // 🚀 UPDATED: Use language from GenAI output instead of utterance manager
                TranscriptionConfidence = utteranceWithContext.TranscriptionConfidence,
                AudioFingerprint = utteranceWithContext.AudioFingerprint // Pass raw DNA for sync
            };

            Console.WriteLine($"TIMESTAMP_SPEAKER_PROCESS_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Processing speaker identification");
            // Pass the GenAI Decision (Identification) + Raw DNA (Payload) to the Manager
            var speakerResult = await _speakerManager.ProcessSpeakerIdentificationAsync(state.SessionId!, speakerPayload);
            
            state.LastSpeakerId = speakerResult.SpeakerId;

            // 🚀 NEW: Store extracted facts to prevent future duplication
            if (genAIResponse.FactExtraction?.RequiresFactExtraction == true && 
                genAIResponse.FactExtraction.Facts?.Count > 0)
            {
                Console.WriteLine($"TIMESTAMP_FACTS_STORAGE_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Storing {genAIResponse.FactExtraction.Facts.Count} new facts");
                await StoreExtractedFactsAsync(state.SessionId!, genAIResponse);
            }

            Console.WriteLine($"TIMESTAMP_PARALLEL_RESPONSES_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting parallel response processing");
            state.StartCompleting();
            
            // 🚀 PARALLEL PROCESSING: Send all responses concurrently
            await SendConversationResponseParallelAsync(connectionId, utteranceWithContext, genAIResponse, speakerResult);
            
            Console.WriteLine($"TIMESTAMP_PARALLEL_RESPONSES_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Parallel response processing completed");
            
            // FILTERABLE: Complete utterance signal sent
            Console.WriteLine($"TIMESTAMP_CYCLE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Cycle completion signal sent");
            await _notificationService.NotifyCycleCompletionAsync(connectionId, false);
            
            // ✅ SIMPLIFIED: Utterance state reset is handled by ResetCycle in finally block
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
        Console.WriteLine($"TIMESTAMP_GENAI_METHOD_START: {DateTime.UtcNow:HH:mm:ss.fff} - ProcessWithGenAI started for '{utterance.Text}'");
        
        // 🚀 FETCH HISTORY: Get last 5 turns and existing facts
        var session = await _sessionRepository.GetByIdAsync(state.SessionId!, CancellationToken.None);
        var recentHistory = new List<ConversationHistoryItem>();
        var existingFacts = new List<string>();
        
        Console.WriteLine($"TIMESTAMP_HISTORY_FETCH_START: {DateTime.UtcNow:HH:mm:ss.fff} - Fetching conversation history");
        
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

            // 🚀 NEW: Get existing facts to prevent duplication
            // TODO: Implement proper fact storage in session or dedicated fact service
            // For now, extract facts from conversation turn metadata as a temporary solution
            existingFacts = session.ConversationHistory
                .Where(t => t.Metadata.ContainsKey("extractedFacts"))
                .SelectMany(t => (t.Metadata["extractedFacts"] as List<string>) ?? new List<string>())
                .Distinct()
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
                ["speakers"] = (await _speakerManager.GetSessionSpeakersAsync(state.SessionId!))
                                .Select(s => new { s.SpeakerId, s.DisplayName, s.Insights.AssignedRole }),
                ["lastSpeaker"] = state.LastSpeakerId ?? "None",
                ["audioProvisionalId"] = utterance.ProvisionalSpeakerId ?? "Unknown",
                ["recentHistory"] = recentHistory,
                ["existingFacts"] = existingFacts  // 🚀 NEW: Add existing facts to prevent duplication
            }
        };

        // 🚀 FEATURES-ONLY FLOW: Inject Comparison Scorecard
        if (utterance.AudioFingerprint != null)
        {
            var candidates = await _speakerManager.GetSessionSpeakersAsync(state.SessionId!);
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
            var speakers = await _speakerManager.GetSessionSpeakersAsync(state.SessionId ?? "");
            var activeSpeaker = speakers.FirstOrDefault(s => s.SpeakerId == (speakerUpdate.SpeakerId ?? state.LastSpeakerId)) 
                ?? new SpeakerProfile { SpeakerId = "unknown", DisplayName = "Unknown Speaker" };

            // 🚀 STEP 1: Process and Send Speaker Update Sequentially (Ensures UI has name before bubble)
            if (speakerUpdate.Success)
            {
                Console.WriteLine($"TIMESTAMP_SPEAKER_SEQUENTIAL_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting speaker profile logic");
                var frontendSpeakerUpdate = _frontendService.CreateSpeakerListUpdate(speakers);
                
                // 📤 CONSOLE LOG: Speaker list data being sent
                Console.WriteLine($"FRONTEND_SPEAKER_LIST_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending speaker list with {speakers.Count} speakers");
                foreach (var speaker in speakers)
                {
                    Console.WriteLine($"  SPEAKER: {speaker.SpeakerId} - {speaker.DisplayName} ({speaker.Gender})");
                }
                
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
                ttsLanguage = translationResponse.AudioLanguage ?? "en";
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

            // Re-create the conversation items with the UPDATED active speaker info and IMPROVED AI data
            var mainConversationItem = _frontendService.CreateFromTranslation(
                new UtteranceWithContext
                {
                    Text = translationResponse.ImprovedTranscription, // 🚀 Use improved transcription from AI
                    DominantLanguage = translationResponse.AudioLanguage ?? "en-US", // 🚀 Use audio language from AI 
                    TranscriptionConfidence = utterance.TranscriptionConfidence, // Keep original confidence
                    ProvisionalSpeakerId = utterance.ProvisionalSpeakerId,
                    AudioFingerprint = utterance.AudioFingerprint,
                    CreatedAt = utterance.CreatedAt
                },
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
                    translationResponse.AudioLanguage ?? "en-US", // 🚀 Use audio language from AI response instead of utterance
                    translationResponse.AIAssistance.Response??string.Empty,
                    translationResponse.AIAssistance.ResponseTranslated,
                    translationResponse.TranslationLanguage ?? "en",
                    1.0f, // AI response assumed high confidence
                );
            }

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    // 📤 CONSOLE LOG: Main conversation item data being sent
                    Console.WriteLine($"FRONTEND_CONVERSATION_ITEM_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending main conversation item");
                    Console.WriteLine($"  SPEAKER: {mainConversationItem.SpeakerName} ({activeSpeaker.SpeakerId})");
                    Console.WriteLine($"  TRANSCRIPTION: '{mainConversationItem.TranscriptionText}'");
                    Console.WriteLine($"  TRANSLATION: '{mainConversationItem.TranslationText}'");
                    Console.WriteLine($"  CONFIDENCE: Transcription={mainConversationItem.TranscriptionConfidence:F2}, Translation={mainConversationItem.TranslationConfidence:F2}");
                    
                    await _notificationService.SendFrontendConversationItemAsync(connectionId, mainConversationItem);
                    
                    // 🚀 NEW: Add main conversation item to session history using language from GenAI output
                    await AddFrontendConversationToHistoryAsync(state.SessionId!, mainConversationItem, activeSpeaker.SpeakerId, translationResponse.AudioLanguage ?? "en-US");
                    
                    if (aiConversationItem != null)
                    {
                        // 📤 CONSOLE LOG: AI conversation item data being sent
                        Console.WriteLine($"FRONTEND_AI_CONVERSATION_ITEM_SEND: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending AI conversation item");
                        Console.WriteLine($"  AI RESPONSE: '{aiConversationItem.TranscriptionText}'");
                        Console.WriteLine($"  AI TRANSLATION: '{aiConversationItem.TranslationText}'");
                        
                        await _notificationService.SendFrontendConversationItemAsync(connectionId, aiConversationItem);
                        
                        // 🚀 NEW: Add AI conversation item to session history using language from GenAI output
                        await AddFrontendConversationToHistoryAsync(state.SessionId!, aiConversationItem, "ai-assistant", translationResponse.AudioLanguage ?? "en-US");
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
                var sessionSpeakers = await _speakerManager.GetSessionSpeakersAsync(state.SessionId);
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
                var sessionSpeakers = await _speakerManager.GetSessionSpeakersAsync(state.SessionId);
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

    private async Task StoreExtractedFactsAsync(string sessionId, dynamic genAIResponse)
    {
        try
        {
            if (genAIResponse?.FactExtraction?.Facts != null)
            {
                var facts = new List<dynamic>(genAIResponse.FactExtraction.Facts);
                if (facts.Any())
                {
                    // Get the session to add facts to conversation history
                    var session = await _sessionRepository.GetByIdAsync(sessionId, CancellationToken.None);
                    if (session != null)
                    {
                        // Create a fact storage turn using domain entity
                        var factTurn = DomainConversationTurn.CreateSpeech(
                            "system", 
                            "System", 
                            $"Extracted {facts.Count} facts from conversation", 
                            "en"
                        ).SetMetadata("extractedFacts", facts);
                        session.AddConversationTurn(factTurn);
                        
                        await _sessionRepository.SaveAsync(session, CancellationToken.None);
                        _logger.LogInformation($"Stored {facts.Count} extracted facts for session {sessionId}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store extracted facts for session {SessionId}", sessionId);
        }
    }

    private async Task AddFrontendConversationToHistoryAsync(string sessionId, FrontendConversationItem conversationItem, string speakerId, string audioLanguage)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId, CancellationToken.None);
            if (session != null)
            {
                // Create conversation turn from frontend item using language from GenAI output
                var conversationTurn = DomainConversationTurn.CreateSpeech(
                    speakerId,
                    conversationItem.SpeakerName ?? "Unknown",
                    conversationItem.TranscriptionText ?? "",
                    audioLanguage // 🚀 Use language from GenAI output instead of utterance manager
                ).SetTranslation(
                    conversationItem.TranslationText ?? "",
                    audioLanguage // Use the same language for target as we don't have separate target language code in FrontendConversationItem
                );

                // Add frontend-specific metadata
                conversationTurn.SetMetadata("frontendResponseType", conversationItem.ResponseType)
                              .SetMetadata("sentToFrontend", DateTime.UtcNow)
                              .SetMetadata("transcriptionConfidence", conversationItem.TranscriptionConfidence)
                              .SetMetadata("translationConfidence", conversationItem.TranslationConfidence)
                              .SetMetadata("speakerConfidence", conversationItem.SpeakerConfidence);

                session.AddConversationTurn(conversationTurn);
                await _sessionRepository.SaveAsync(session, CancellationToken.None);
                
                _logger.LogInformation($"Added frontend conversation item to history for session {sessionId}, speaker {speakerId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add frontend conversation to history for session {SessionId}", sessionId);
        }
    }

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
    
    // ⚡ IMMEDIATE CANCELLATION: Store cancellation token for instant STT stopping
    public CancellationTokenSource? CurrentCycleCts { get; set; }
    
    // ⚡ DOUBLE-PROCESSING PREVENTION: Track if utterance is already being processed
    public bool IsProcessingStarted { get; set; } = false;
    
    // ✅ SIMPLIFIED: Direct utterance state (no separate collector needed)
    public List<string> FinalUtterances { get; } = new();
    public List<TranscriptionResult> AllResults { get; } = new();
    public Dictionary<string, int> LanguageVotes { get; } = new();
    public List<float> ConfidenceScores { get; } = new();
    public string CurrentInterimText { get; set; } = string.Empty;
    public bool IsUtteranceCompleted { get; set; } = false;
    public string? ProvisionalSpeakerId { get; set; }
    public float SpeakerMatchConfidence { get; set; } = 0f;
    public AudioFingerprint? AccumulatedAudioFingerprint { get; set; }
    
    public string[] CandidateLanguages => new[] { PrimaryLanguage ?? "en-US", SecondaryLanguage ?? "en-GB" };
    
    // STRICT DESIGN: Only accept audio when Ready (new cycle) or actively receiving (before VAD timeout)
    // Once VAD triggers ProcessingUtterance/SendingResponse, reject ALL new chunks until cycle completes
    public bool CanAcceptAudio => CycleState == ConversationPhase.Ready || CycleState == ConversationPhase.ReceivingAudio;
    
    public bool ShouldStartNewCycle => CycleState == ConversationPhase.Ready;
    
    public bool HasAccumulatedText => FinalUtterances.Any();
    
    public ConversationState(string connectionId) => ConnectionId = connectionId;

    public void CacheSessionConfig(string sessionId, string primary, string secondary)
    {
        SessionId = sessionId;
        PrimaryLanguage = primary;
        SecondaryLanguage = secondary;
    }

    /// <summary>
    /// Add transcription result from STT service
    /// </summary>
    public void AddTranscriptionResult(TranscriptionResult result)
    {
        Console.WriteLine($"TIMESTAMP_TRANSCRIPTION_RECEIVED: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - STT result received: '{result.Text}' IsFinal: {result.IsFinal}");
        
        if (IsUtteranceCompleted)
        {
            Console.WriteLine($"TIMESTAMP_TRANSCRIPTION_IGNORED: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Ignoring STT result as utterance already completed");
            return; // Ignore results after completion
        }

        AllResults.Add(result);
        ConfidenceScores.Add((float)result.Confidence);

        if (result.IsFinal)
        {
            Console.WriteLine($"TIMESTAMP_FINAL_RESULT_PROCESSING: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Processing final STT result");
            ProcessFinalResult(result);
        }
        else
        {
            Console.WriteLine($"TIMESTAMP_INTERIM_RESULT_PROCESSING: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Processing interim STT result");
            UpdateInterimResult(result);
        }
    }

    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// </summary>
    public void CompleteUtterance()
    {
        Console.WriteLine($"TIMESTAMP_COMPLETE_UTTERANCE_CALLED: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - CompleteUtterance() called by frontend VAD");
        
        IsUtteranceCompleted = true;
        
        // Add any pending interim text as final
        if (!string.IsNullOrWhiteSpace(CurrentInterimText))
        {
            Console.WriteLine($"TIMESTAMP_INTERIM_TO_FINAL: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Converting interim text to final: '{CurrentInterimText}'");
            FinalUtterances.Add(CurrentInterimText.Trim());
            CurrentInterimText = string.Empty;
        }
        
        Console.WriteLine($"TIMESTAMP_COMPLETE_UTTERANCE_FINISHED: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - CompleteUtterance() finished, final utterances: {FinalUtterances.Count}");
    }

    /// <summary>
    /// Get current display text for real-time UI updates
    /// </summary>
    public string GetCurrentDisplayText()
    {
        var accumulated = GetAccumulatedText();
        if (!string.IsNullOrWhiteSpace(CurrentInterimText))
        {
            return string.IsNullOrWhiteSpace(accumulated) 
                ? CurrentInterimText 
                : $"{accumulated} {CurrentInterimText}";
        }
        return accumulated;
    }

    /// <summary>
    /// Get complete utterance with resolved languages and speaker context
    /// </summary>
    public UtteranceWithContext GetCompleteUtterance()
    {
        Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_START: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - GetCompleteUtterance() started");
        
        if (!IsUtteranceCompleted || !FinalUtterances.Any())
        {
            Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Utterance not ready: Completed={IsUtteranceCompleted}, HasFinal={FinalUtterances.Any()}");
            throw new InvalidOperationException("Utterance not completed or no content available");
        }

        Console.WriteLine($"TIMESTAMP_LANGUAGE_RESOLUTION_START: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Starting language resolution");
        
        var dominantLanguage = ResolveDominantLanguage();
        var (sourceLanguage, targetLanguage) = ResolveSourceTargetLanguages(
            dominantLanguage, CandidateLanguages, PrimaryLanguage!);
        var averageConfidence = CalculateAverageConfidence();

        var result = new UtteranceWithContext
        {
            Text = GetAccumulatedText(),
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            DominantLanguage = dominantLanguage,
            TranscriptionConfidence = averageConfidence,
            ProvisionalSpeakerId = ProvisionalSpeakerId,
            SpeakerConfidence = SpeakerMatchConfidence,
            DetectionResults = AllResults.ToList(),
            AudioFingerprint = AccumulatedAudioFingerprint,
            CreatedAt = DateTime.UtcNow
        };
        
        Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - GetCompleteUtterance() complete: '{result.Text}' {result.SourceLanguage}→{result.TargetLanguage}");
        return result;
    }

    /// <summary>
    /// Set speaker identification context
    /// </summary>
    public void SetSpeakerContext(string? speakerId, float confidence, AudioFingerprint? fingerprint = null)
    {
        ProvisionalSpeakerId = speakerId;
        SpeakerMatchConfidence = confidence;
        AccumulatedAudioFingerprint = fingerprint;
    }

    /// <summary>
    /// Reset utterance state for new conversation cycle
    /// </summary>
    public void ResetUtteranceState()
    {
        FinalUtterances.Clear();
        CurrentInterimText = string.Empty;
        LanguageVotes.Clear();
        AllResults.Clear();
        ConfidenceScores.Clear();
        ProvisionalSpeakerId = null;
        SpeakerMatchConfidence = 0f;
        AccumulatedAudioFingerprint = null;
        IsUtteranceCompleted = false;
    }

    public void StartReceivingAudio() => CycleState = ConversationPhase.ReceivingAudio;
    public void StartProcessing() => CycleState = ConversationPhase.ProcessingUtterance;
    public void StartCompleting() => CycleState = ConversationPhase.SendingResponse;
    
    /// <summary>
    /// ⚡ IMMEDIATE CANCELLATION: Cancel current STT processing cycle immediately
    /// </summary>
    public void CancelCurrentCycle()
    {
        if (CurrentCycleCts != null && !CurrentCycleCts.IsCancellationRequested)
        {
            CurrentCycleCts.Cancel();
        }
    }
    
    public void ResetCycle() 
    { 
        CycleState = ConversationPhase.Ready;
        IsProcessingStarted = false; // Reset processing flag for next cycle
        CurrentCycleCts?.Dispose();
        CurrentCycleCts = null;
        ResetUtteranceState();
    }
    
    public void Dispose() => AudioStreamChannel.Writer.TryComplete();

    // --- Private Helper Methods ---

    private string GetAccumulatedText()
    {
        return string.Join(" ", FinalUtterances).Trim();
    }

    private void ProcessFinalResult(TranscriptionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            FinalUtterances.Add(result.Text.Trim());
            TrackLanguageVote(result.Language);
        }
        CurrentInterimText = string.Empty;
    }

    private void UpdateInterimResult(TranscriptionResult result)
    {
        CurrentInterimText = result.Text ?? string.Empty;
        TrackLanguageVote(result.Language);
    }

    private void TrackLanguageVote(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        LanguageVotes[language] = LanguageVotes.GetValueOrDefault(language, 0) + 1;
    }

    private string ResolveDominantLanguage()
    {
        if (LanguageVotes.Count == 0)
            return AllResults.FirstOrDefault()?.Language ?? "en-US";

        return LanguageVotes
            .OrderByDescending(kvp => kvp.Value)
            .First()
            .Key;
    }

    private (string sourceLanguage, string targetLanguage) ResolveSourceTargetLanguages(
        string dominantLanguage, 
        string[] candidateLanguages, 
        string sessionPrimaryLanguage)
    {
        // Rule 1: Dominant language is in candidates
        if (candidateLanguages.Contains(dominantLanguage))
        {
            var otherCandidates = candidateLanguages.Where(c => c != dominantLanguage).ToArray();
            var targetLanguage = otherCandidates.FirstOrDefault() ?? sessionPrimaryLanguage;
            return (dominantLanguage, targetLanguage);
        }

        // Rule 2: Check if secondary detected language is in candidates
        var secondaryLanguage = LanguageVotes
            .Where(kvp => kvp.Key != dominantLanguage)
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault()
            .Key;

        if (!string.IsNullOrEmpty(secondaryLanguage) && candidateLanguages.Contains(secondaryLanguage))
        {
            var otherCandidates = candidateLanguages.Where(c => c != secondaryLanguage).ToArray();
            var targetLanguage = otherCandidates.FirstOrDefault() ?? sessionPrimaryLanguage;
            return (secondaryLanguage, targetLanguage);
        }

        // Rule 3: Fallback - use detected as source, session primary as target
        return (dominantLanguage, sessionPrimaryLanguage);
    }

    private float CalculateAverageConfidence()
    {
        if (ConfidenceScores.Count == 0) return 0f;
        return ConfidenceScores.Average();
    }
}
