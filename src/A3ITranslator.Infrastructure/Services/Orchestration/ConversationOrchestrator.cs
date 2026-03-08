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
using System.Text;
using System.Linq;
using System.Collections.Concurrent;
using A3ITranslator.Application.Models;
using System.Text.Json;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Infrastructure.Services.Translation; // For JsonStreamParser
using System.Diagnostics;

// ✅ PURE DOMAIN: Type aliases for clean architecture
using DomainSession = A3ITranslator.Application.Domain.Entities.ConversationSession;
using DomainConversationTurn = A3ITranslator.Application.Domain.Entities.ConversationTurn;

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
    private readonly IAudioFeatureExtractor _featureExtractor;
    private readonly ITranscriptionManager _transcriptionManager;
    private readonly ISpeakerSyncService _speakerSyncService;
    private readonly IConversationLifecycleManager _lifecycleManager;
    private readonly ITranslationService _translationService;
    private readonly IConversationResponseService _responseService;
    private readonly IMetricsService _metricsService;
    private readonly ITranslationOrchestrator _translationOrchestrator;
    private readonly IGenAIService _genAIService;
    private readonly IAzureTextTranslatorService _textTranslatorService;

    // Per-connection conversation state
    private readonly Dictionary<string, ConversationState> _connectionStates = new();
    private readonly object _stateLock = new();

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> logger,
        IRealtimeNotificationService notificationService,
        IStreamingTTSService ttsService,
        ISessionRepository sessionRepository,
        IStreamingSTTService sttService,
        IAudioFeatureExtractor featureExtractor,
        ITranscriptionManager transcriptionManager,
        ISpeakerSyncService speakerSyncService,
        IConversationLifecycleManager lifecycleManager,
        ITranslationService translationService,
        IConversationResponseService responseService,
        IMetricsService metricsService,
        ITranslationOrchestrator translationOrchestrator,
        IGenAIService genAIService,
        IAzureTextTranslatorService textTranslatorService)
    {
        _notificationService = notificationService;
        _ttsService = ttsService;
        _sessionRepository = sessionRepository;
        _sttService = sttService;
        _featureExtractor = featureExtractor;
        _transcriptionManager = transcriptionManager;
        _speakerSyncService = speakerSyncService;
        _lifecycleManager = lifecycleManager;
        _translationService = translationService;
        _responseService = responseService;
        _metricsService = metricsService;
        _translationOrchestrator = translationOrchestrator;
        _genAIService = genAIService;
        _textTranslatorService = textTranslatorService;
        _logger = logger;
    }

    /// <summary>
    /// Main entry point: Process audio chunk with proper state management
    /// Follows the original design: Accept chunks only when Ready, process with VAD timeout
    /// </summary>
    public async Task ProcessAudioChunkAsync(string connectionId, byte[] audioChunk)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        Console.WriteLine($"TIMESTAMP_AUDIO_CHUNK_EVAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - CanAcceptAudio: {state.CanAcceptAudio}, CycleState: {state.CycleState}, ChunkSize: {audioChunk.Length}");

        // 🛡️ ZERO-LOSS AUDIO: If we are busy, buffer the chunk for the next cycle
        if (!state.CanAcceptAudio)
        {
            state.BufferPendingChunk(audioChunk);
            return;
        }
        
        // FILTERABLE: Audio chunk received
        Console.WriteLine($"TIMESTAMP_AUDIO_CHUNK: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {audioChunk.Length} bytes");

        // 🔍 HEADER CAPTURE: Always check for and store the WebM header (starts with 1A 45 DF A3)
        if (IsWebmHeader(audioChunk))
        {
            state.AudioHeader = audioChunk;
            _logger.LogDebug("📑 Captured WebM/Opus header for connection {ConnectionId}", connectionId);
        }

        // Start NEW cycle only when explicitly Ready (after previous cycle completed)
        if (state.ShouldStartNewCycle)
        {
            // 🛡️ STATE REFRESH: Ready for new cycle
            state.CycleState = ConversationPhase.ReceivingAudio;
            state.IsProcessingStarted = false;
            state.CycleStartTime = DateTime.UtcNow;
            
            // 🔄 REFRESH CHANNEL: Synchronously reset for immediate writing
            state.ResetAudioChannel();
            
            // 📑 INJECT HEADER: Ensure Google/Azure STT can decode mid-stream chunks
            if (state.AudioHeader != null)
            {
                state.AudioStreamChannel.Writer.TryWrite(state.AudioHeader);
                // If the current chunk is the header, don't write it again
                if (IsWebmHeader(audioChunk))
                {
                    _ = Task.Run(() => ProcessConversationPipelineAsync(connectionId, state));
                    return;
                }
            }
            
            // 📥 DRAIN BUFFER: Write any chunks that arrived during the transition
            while (state.TryDequeuePendingChunk(out var pendingChunk))
            {
                // 🛡️ HEADER GUARD: Skip redundant headers in buffer if we already injected one
                if (state.AudioHeader != null && IsWebmHeader(pendingChunk)) continue;
                state.AudioStreamChannel.Writer.TryWrite(pendingChunk);
            }

            // Write the current triggering chunk
            var writeResult = state.AudioStreamChannel.Writer.TryWrite(audioChunk);
            Console.WriteLine($"TIMESTAMP_AUDIO_CHANNEL_TRYWRITE_NEW_CYCLE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - WriteResult: {writeResult}, ChunkSize: {audioChunk.Length}");
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
            // If writer is closed (draining), buffer for next cycle
            var existingWriteResult = state.AudioStreamChannel.Writer.TryWrite(audioChunk);
            Console.WriteLine($"TIMESTAMP_AUDIO_CHANNEL_TRYWRITE_EXISTING_CYCLE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - WriteResult: {existingWriteResult}, ChunkSize: {audioChunk.Length}");
            if (!existingWriteResult)
            {
                state.BufferPendingChunk(audioChunk);
                _logger.LogDebug("📥 Pipeline draining, buffered chunk for next cycle for {ConnectionId}", connectionId);
            }
        }
    }

    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// ELEGANT APPROACH: Close stream and let STT finish naturally
    /// </summary>
    public async Task CompleteUtteranceAsync(string connectionId)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        // 🛡️ GUARD: Ignore VAD signals if we are not actively receiving audio
        // This prevents late VAD signals from previous cycles from messing up the next cycle
        if (state.CycleState == ConversationPhase.Ready)
        {
            _logger.LogDebug("⚠️ ORCHESTRATOR: Ignored late VAD completion signal for {ConnectionId} (State is Ready)", connectionId);
            return;
        }

        _logger.LogInformation("🔇 Frontend VAD: Utterance completion signal received for {ConnectionId}", connectionId);
        
        Console.WriteLine($"TIMESTAMP_UTTERANCE_COMPLETION_SIGNAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Frontend VAD detected silence");
        
        // Mark utterance as completed (stops winner monitoring loop)
        state.CompleteUtterance();
        
        // ⚡ CRITICAL: Complete the writer to signal 'end of stream' to STT broadcasters
        // This allows the Dual broadcaster and Google/Azure STT streams to drain and finish naturally.
        state.AudioStreamChannel.Writer.TryComplete();
        
        _logger.LogDebug("✅ Utterance completion marked for {ConnectionId}. Pipeline will drain lingering chunks.", connectionId);
        
        // Processing will happen in ProcessConversationPipelineAsync after grace period
        await Task.CompletedTask;
    }

    /// <summary>
    /// Cancel the current conversation cycle and reset for next input
    /// </summary>
    public async Task CancelUtteranceAsync(string connectionId)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        // 🛡️ GUARD: Ignore cancel signals if we are not in an active cycle
        if (state.CycleState == ConversationPhase.Ready)
        {
            _logger.LogDebug("⚠️ ORCHESTRATOR: Ignored redundant CancelUtterance for {ConnectionId} (State is already Ready)", connectionId);
            return;
        }

        _logger.LogInformation("🛑 Frontend CANCEL: CancelUtterance signal received for {ConnectionId}", connectionId);
        Console.WriteLine($"TIMESTAMP_CANCEL_UTTERANCE_SIGNAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Frontend requested cancellation");

        // 1. Immediately cancel any active processing in this cycle
        state.CancelCurrentCycle();
        
        // 2. Clear current progress text so pipeline doesn't process it as it drains
        state.UtteranceManager.ResetUtteranceState();
        
        // 🧹 PURGE ZERO-LOSS BUFFER: On manual cancel, we throw away all buffered audio
        while (state.TryDequeuePendingChunk(out _)) { }
        
        // 3. Complete the audio channel to stop any pending reads
        // This triggers the broadcaster to stop and leads to the finally block for cleanup
        state.AudioStreamChannel.Writer.TryComplete();
        
        // 4. Notify frontend that we are ready again
        await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
        
        _logger.LogDebug("✅ Conversation cycle cancellation signaled for {ConnectionId}. State will reset in pipeline cleanup.", connectionId);
        
        await Task.CompletedTask;
    }

    private static bool IsWebmHeader(byte[] chunk)
    {
        // EBML/WebM header starts with 1A 45 DF A3
        return chunk != null && chunk.Length >= 4 && 
               chunk[0] == 0x1A && chunk[1] == 0x45 && chunk[2] == 0xDF && chunk[3] == 0xA3;
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
            var sttPrimaryChannel = Channel.CreateUnbounded<byte[]>();
            var sttSecondaryChannel = Channel.CreateUnbounded<byte[]>();
            
            // ✨ NEW: Background Rolling Feature Accumulator (Zero Latency)
            var rollingFingerprint = new AudioFingerprint();

            // 2. Start parallel processing tasks
            // CRITICAL: Start broadcaster FIRST so audio flows to channels before consumers start
            Console.WriteLine($"TASK_START_BroadcastAudioAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting dual broadcaster task");
            var broadcasterTask = BroadcastAudioToDualChannelsAsync(
                state.AudioStreamChannel.Reader, 
                sttPrimaryChannel, 
                sttSecondaryChannel,
                state,
                connectionId, 
                cycleCts.Token);
            
            // ✨ NEW: Neural Speaker Identification Task (Periodic Updates)
            var speakerIdTask = MonitorSpeakerIdentificationAsync(connectionId, state, cycleCts.Token);
            
            Console.WriteLine($"TASK_START_TranscriptionCompetition: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId}");
            var competitionResult = await _transcriptionManager.RunCompetitionAsync(
                sttPrimaryChannel.Reader,
                sttSecondaryChannel.Reader,
                connectionId,
                session.PrimaryLanguage,
                session.SecondaryLanguage ?? "en-US",
                () => state.IsUtteranceCompleted,
                async (bestText, result) => {
                    state.AddTranscriptionResult(result);
                    await _notificationService.NotifyTranscriptionAsync(connectionId, bestText, result.Language, false);
                },
                cycleCts.Token
            );

            // 🏆 ADOPT WINNER
            _logger.LogInformation("🏆 Competition winner: {Language} with confidence {Confidence:F2}", 
                competitionResult.WinnerLanguage, competitionResult.Confidence);

            if (!string.IsNullOrEmpty(competitionResult.LoserBestText))
            {
                _logger.LogInformation("💀 Competition loser: {Language} with confidence {Confidence:F2}. Text: '{Text}'",
                    competitionResult.LoserLanguage, competitionResult.LoserConfidence, competitionResult.LoserBestText);
            }
            
            state.ProcessingLanguage = competitionResult.WinnerLanguage;
            // The state.AddTranscriptionResult already happened during monitoring for winner if it was selected early, 
            // but we need to ensure ALL results from the winner are in the state's UtteranceManager.
            // Since our ITranscriptionManager implementation returns AllResults, we can just sync them.
            state.UtteranceManager.ResetUtteranceState();
            foreach (var res in competitionResult.AllResults)
            {
                state.AddTranscriptionResult(res);
            }

            // Cancel broadcaster and other tasks
            if (!cycleCts.IsCancellationRequested) cycleCts.Cancel();

            // 4. ⚡ PROCESSING OPTIMIZATION: Process immediately if we have valid utterance data
            if (state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                _logger.LogInformation("🎯 Frontend VAD completion detected - processing utterance immediately for {ConnectionId}", connectionId);
                
                // 🔄 PHASE TRANSITION: ReceivingAudio → ProcessingUtterance
                state.StartProcessing();
                
                // Final neural embedding extraction
                var finalEmbedding = await _featureExtractor.ExtractEmbeddingAsync(connectionId);
                state.SetSpeakerContext(state.ProvisionalSpeakerId, state.ProvisionalDisplayName, 0f, new AudioFingerprint { Embedding = finalEmbedding });
                
                await ProcessCompletedUtteranceAsync(connectionId, state);
            }
            else if (!state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                _logger.LogInformation("🔄 STT channel closed with text but no frontend signal - auto-completing utterance for {ConnectionId}", connectionId);
                state.CompleteUtterance(); // Mark as completed

                // 🔄 PHASE TRANSITION
                state.StartProcessing();
                
                var finalEmbedding = await _featureExtractor.ExtractEmbeddingAsync(connectionId);
                state.SetSpeakerContext(state.ProvisionalSpeakerId, state.ProvisionalDisplayName, 0f, new AudioFingerprint { Embedding = finalEmbedding });
                
                await ProcessCompletedUtteranceAsync(connectionId, state);
            }
            else
            {
                _logger.LogDebug("✅ No accumulated text to process for {ConnectionId}", connectionId);
                // 🛑 CRITICAL FIX: Tell the frontend the cycle is complete even if we had no text!
                // Otherwise the frontend gets permanently stuck waiting for a response that will never come.
                await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
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
            // 🏁 CYCLE END: Reset for next cycle ONLY if this is still the active cycle ownership
            // This prevents a late-running previous cycle from wiping out a new cycle's state.
            if (state.CurrentCycleCts == cycleCts)
            {
                state.ResetCycle();
                _logger.LogDebug("🔄 CYCLE END: Ready for next conversation cycle on {ConnectionId}", connectionId);
                
                // 🧹 Clear neural audio buffer for this connection
                // 🛡️ Ownership Guard: Only clear if this cycle is still in control
                _featureExtractor.ClearBuffer(connectionId);
            }
            else
            {
                _logger.LogDebug("🎬 CYCLE OVERTAKEN: skipping reset for {ConnectionId} as a new cycle is already active", connectionId);
            }
        }
    }

    /// <summary>
    /// Background task to monitor audio and perform provisional speaker identification
    /// </summary>
    private async Task MonitorSpeakerIdentificationAsync(string connectionId, ConversationState state, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🧬 Starting neural speaker identification monitor for {ConnectionId}", connectionId);
            
            // Wait for enough audio to accumulate (min 1.5 seconds)
            await Task.Delay(1500, cancellationToken);
            
            while (!cancellationToken.IsCancellationRequested && !state.IsUtteranceCompleted)
            {
                var (speakerId, displayName, confidence, fingerprint) = await _speakerSyncService.IdentifySpeakerAsync(
                    state.SessionId!, connectionId, null!, cancellationToken);

                if (speakerId != null)
                {
                    _logger.LogInformation("🎯 PROVISIONAL ID: Identified {Speaker} with {Score:P0} consistency", 
                        displayName, confidence);
                    
                    // Notify frontend about the provisional speaker
                    await _notificationService.NotifyProcessingStatusAsync(connectionId, $"👤 Identified: {displayName}");
                    
                    // Update state so the GenAI knows who we think is talking
                    state.ProvisionalSpeakerId = speakerId;
                    state.ProvisionalDisplayName = displayName;
                    state.SpeakerMatchConfidence = confidence;
                }
                
                // Update every 1.5 seconds
                await Task.Delay(1500, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in neural speaker monitor for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Broadcast audio to dual STT processing channels for language competition
    /// </summary>
    private async Task BroadcastAudioToDualChannelsAsync(
        ChannelReader<byte[]> audioReader, 
        Channel<byte[]> primaryChannel,
        Channel<byte[]> secondaryChannel,
        ConversationState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var chunkCount = 0;
        try
        {
            Console.WriteLine($"TASK_START_BroadcastAudioToDualChannelsAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual broadcaster starting audio processing");
            
            // Read until channel is closed OR until this cycle is cancelled (VAD detected)
            while (await audioReader.WaitToReadAsync(cancellationToken))
            {
                while (audioReader.TryRead(out var chunk))
                {
                    chunkCount++;
                    
                    // 🎯 DUPLICATE AUDIO: Send same chunk to both STT channels for language competition
                    await primaryChannel.Writer.WriteAsync(chunk, cancellationToken);
                    await secondaryChannel.Writer.WriteAsync(chunk, cancellationToken);
                    
                    // 📊 TRACK AUDIO DURATION (16kHz, 16-bit Mono = 32000 bytes/sec)
                    state.AccumulatedAudioSec += (double)chunk.Length / 32000.0;
                    
                    // ✨ Neural Feature Extraction (Buffering for ONNX)
                    _ = _featureExtractor.AccumulateAudioAsync(connectionId, chunk);
                }
            }
            Console.WriteLine($"TASK_END_BroadcastAudioToDualChannelsAsync_EXECUTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual broadcaster completed normally after {chunkCount} chunks");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"TASK_CANCELLED_BroadcastAudioToDualChannelsAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual broadcaster cancelled after {chunkCount} chunks");
            _logger.LogDebug("🎬 DUAL BROADCASTER: Cycle cancelled after {ChunkCount} chunks for {ConnectionId}", chunkCount, connectionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TASK_ERROR_BroadcastAudioToDualChannelsAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual broadcaster error after {chunkCount} chunks: {ex.Message}");
            _logger.LogError(ex, "❌ DUAL BROADCASTER: Error after {ChunkCount} chunks for {ConnectionId}", chunkCount, connectionId);
        }
        finally
        {
            primaryChannel.Writer.TryComplete();
            secondaryChannel.Writer.TryComplete();
            Console.WriteLine($"TASK_FINALLY_BroadcastAudioToDualChannelsAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual broadcaster cleanup completed");
        }
    }



    /// <summary>
    /// Process speaker identification with "First-Capture-Locking" for zero latency
    /// </summary>

    private async Task ProcessCompletedUtteranceAsync(
        string connectionId, 
        ConversationState state)
    {
        string turnId = Guid.NewGuid().ToString();
        try
        {
            _logger.LogInformation("🎯 {TurnId}: Processing parallel Micro-Agent cycle for {ConnectionId}", turnId, connectionId);

            var utteranceWithContext = state.GetCompleteUtterance();
            var session = await _sessionRepository.GetByIdAsync(state.SessionId!, CancellationToken.None);
            
            var request = await _translationService.CreateTranslationRequestAsync(
                state.SessionId!, 
                utteranceWithContext, 
                state.LastSpeakerId, 
                state.ProvisionalSpeakerId, 
                state.ProvisionalDisplayName,
                turnId,
                true);

            // 1. Build Prompts for ALL Agents
            _logger.LogInformation("🎯 {TurnId}: Building Agent prompts...", turnId);
            var agent2Prompts = await _translationOrchestrator.BuildAgent2PromptsAsync(request);
            var agent3Prompts = await _translationOrchestrator.BuildAgent3PromptsAsync(request);

            state.GenAIStartTime = DateTime.UtcNow;

            // 2. PARALLEL FAST PATHS: Fire Intent Router & Azure MNT           
            string sourceLangCode = utteranceWithContext.SourceLanguage.Split('-').FirstOrDefault() ?? "en";
            string targetLangCode = utteranceWithContext.TargetLanguage.Split('-').FirstOrDefault() ?? "en";
            
            var mntStartTime = DateTime.UtcNow;
            var mntTask = _textTranslatorService.TranslateTextAsync(utteranceWithContext.Text, sourceLangCode, targetLangCode);

            // 3. BACKGROUND PATH: Fire Agent 2 (Super Agent) and Agent 3
            _logger.LogInformation("🎯 {TurnId}: Firing Agent 2 (Super Agent) and Agent 3 in background...", turnId);
            
            var agent2StartTime = DateTime.UtcNow;
            var agent2Task = _genAIService.GenerateResponseAsync(agent2Prompts.systemPrompt, agent2Prompts.userPrompt);

            // 2. RUN TEXT TRANSLATION (MNT) IMMEDIATELY
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string mntTranslation = await mntTask;
            var mntEndTime = DateTime.UtcNow;
            stopwatch.Stop();
            
            _logger.LogInformation("⚡ {TurnId}: MNT Translation Finished: {Text}", turnId, mntTranslation);

            // Log MNT Metrics
            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                SessionId = state.SessionId ?? "",
                ConnectionId = connectionId,
                Category = ServiceCategory.Translation,
                Provider = "Azure",
                Operation = "MNT_TextTranslation",
                UserPrompt = utteranceWithContext.Text,
                Response = mntTranslation,
                LatencyMs = (long)stopwatch.ElapsedMilliseconds,
                TurnId = turnId,
                Track = "MNT",
                StartTime = mntStartTime,
                EndTime = mntEndTime
            });
           // Fire Agent 3 as detached background "Fire and Forget" task
            _ = Task.Run(async () => 
            {
                try 
                {
                    _logger.LogInformation("🚀 {TurnId}: Agent 3 (Fact Manager) started in asynchronous background...", turnId);
                    
                    var agent3StartTime = DateTime.UtcNow;
                    var factResponse = await _genAIService.GenerateResponseAsync(agent3Prompts.systemPrompt, agent3Prompts.userPrompt);
                    var agent3EndTime = DateTime.UtcNow;

                    _ = _metricsService.LogMetricsAsync(new UsageMetrics
                    {
                        SessionId = state.SessionId ?? "",
                        ConnectionId = connectionId,
                        Category = ServiceCategory.Translation,
                        Provider = _genAIService.GetServiceName(),
                        Model = factResponse.Model,
                        Operation = "Agent3_FactExtraction",
                        UserPrompt = agent3Prompts.userPrompt,
                        Response = factResponse.Content,
                        InputUnits = factResponse.Usage.InputTokens,
                        OutputUnits = factResponse.Usage.OutputTokens,
                        InputUnitType = "Tokens",
                        OutputUnitType = "Tokens",
                        TurnId = turnId,
                        Track = "Agent3",
                        StartTime = agent3StartTime,
                        EndTime = agent3EndTime,
                        LatencyMs = (long)(agent3EndTime - agent3StartTime).TotalMilliseconds
                    });

                    var cleanedJson = CleanJsonStructure(factResponse.Content);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    Agent3Response? agent3Data = null;
                    try
                    {
                        agent3Data = JsonSerializer.Deserialize<Agent3Response>(cleanedJson, options);
                        _logger.LogInformation("✅ {TurnId}: Agent 3 completed fact extraction ({Count} new facts).", turnId, agent3Data?.FactExtraction?.Facts?.Count ?? 0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ {TurnId}: Failed to parse Agent 3 JSON.", turnId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ {TurnId}: Agent 3 background task failed.", turnId);
                }
            });

            var fastResponse = new EnhancedTranslationResponse 
            { 
                Translation = mntTranslation, 
                // CRITICAL FIX: Use the actual calculated target language, NOT the session's fallback language
                TranslationLanguage = utteranceWithContext.TargetLanguage,
                Intent = "SIMPLE_TRANSLATION" 
            };
            
            // 3. FIRE FAST-TWITCH TTS IMMEDIATELY
            _logger.LogInformation("⚡ Fast-Twitch TTS (Azure MNT): Firing instant translation audio in background.");
            _ = Task.Run(async () => {
                try {
                    await _responseService.SendPulseAudioOnlyAsync(connectionId, state.SessionId!, state.ProvisionalSpeakerId ?? state.LastSpeakerId, fastResponse);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Background TTS task failed.");
                }
            });

            // 4. PREPARE DETACHED BACKGROUND PROCESSING
            // We lock the cycle (microphone) until Agent 2 decides if AI Assistance is required, 
            // and generates the corresponding conversation items.

            // 5. TTS ROUTING AND DETACHED BACKGROUND PROCESSING
            Func<Task> processAgent2BackgroundAsync = async () =>
            {
                bool cycleUnblocked = false;
                try
                {
                    var agent2ResponseRaw = await agent2Task;
                    var agent2EndTime = DateTime.UtcNow;

                    _ = _metricsService.LogMetricsAsync(new UsageMetrics
                    {
                        SessionId = state.SessionId ?? "",
                        ConnectionId = connectionId,
                        Category = ServiceCategory.SpeakerID,
                        Provider = _genAIService.GetServiceName(),
                        Model = agent2ResponseRaw.Model,
                        Operation = "Agent2_SuperAgent",
                        UserPrompt = agent2Prompts.userPrompt,
                        Response = agent2ResponseRaw.Content,
                        InputUnits = agent2ResponseRaw.Usage?.InputTokens ?? 0,
                        OutputUnits = agent2ResponseRaw.Usage?.OutputTokens ?? 0,
                        InputUnitType = "Tokens",
                        OutputUnitType = "Tokens",
                        TurnId = turnId,
                        Track = "Agent2",
                        StartTime = agent2StartTime,
                        EndTime = agent2EndTime,
                        LatencyMs = (long)(agent2EndTime - agent2StartTime).TotalMilliseconds
                    });

                    var cleanedJson2 = CleanJsonStructure(agent2ResponseRaw.Content);
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    Agent2Response agent2Data;
                    try
                    {
                        agent2Data = JsonSerializer.Deserialize<Agent2Response>(cleanedJson2, jsonOptions) ?? new Agent2Response();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ {TurnId}: Failed to parse Super Agent 2 JSON. Using empty fallback.", turnId);
                        agent2Data = new Agent2Response();
                    }

                    state.GenAIEndTime = DateTime.UtcNow;

                    // Dynamically resolve Intent based solely on Agent 2 output
                    string intentStr = "SIMPLE_TRANSLATION";
                    if (agent2Data.AIAssistance != null && !string.IsNullOrWhiteSpace(agent2Data.AIAssistance.Response))
                    {
                        intentStr = "AI_ASSISTANCE";
                    }

                    // UNBLOCK or Notify based on intent
                    if (intentStr == "SIMPLE_TRANSLATION")
                    {
                        // Immediate unblock for simple translation
                        await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
                        state.ResetCycle();
                        cycleUnblocked = true;
                    }
                    else
                    {
                        // Signal AI is thinking while we process AI audio
                        await _notificationService.NotifyProcessingStatusAsync(connectionId, "🧠 Tolk is thinking...");
                    }

                    // 7. Resolve Final Responses
                    string? aiAssistanceResponse = null;
                    if (intentStr == "AI_ASSISTANCE" && agent2Data.AIAssistance != null)
                    {
                        aiAssistanceResponse = !string.IsNullOrWhiteSpace(agent2Data.AIAssistance.ResponseTranslated) 
                            ? agent2Data.AIAssistance.ResponseTranslated 
                            : agent2Data.AIAssistance.Response;
                        
                        _logger.LogInformation("⚡ {TurnId}: Heavy-Context: Preparing AI Contextual Assistant audio for SendResponseAsync.", turnId);
                    }

                    // The main visual element uses Super Agent 2 data (No MNT Merge needed for UI text)
                    var baseResponse = new EnhancedTranslationResponse
                    {
                        ImprovedTranscription = agent2Data.ImprovedTranscription, 
                        Translation = string.IsNullOrWhiteSpace(agent2Data.Translation) ? mntTranslation : agent2Data.Translation, // Use Super Agent 2's translation, fallback to MNT if empty
                        Intent = "SIMPLE_TRANSLATION", // Force base item to be regular translation
                        TranslationLanguage = utteranceWithContext.TargetLanguage,
                        AudioLanguage = !string.IsNullOrWhiteSpace(utteranceWithContext.SourceLanguage) ? utteranceWithContext.SourceLanguage : request.SourceLanguage,
                        EstimatedGender = agent2Data.EstimatedGender,
                        Confidence = agent2Data.Confidence,
                        TurnAnalysis = agent2Data.TurnAnalysis,
                        SessionRoster = agent2Data.SessionRoster,
                        FactExtraction = new FactExtractionPayload(), // Detached Agent 3
                        Usage = new GenAIUsage
                        {
                            InputTokens = agent2ResponseRaw.Usage?.InputTokens ?? 0,
                            OutputTokens = agent2ResponseRaw.Usage?.OutputTokens ?? 0
                        }
                    };

                    // 5. SPEAKER IDENTIFICATION & STATE UPDATE
                    var speakerResult = await SyncSpeakerAndFactsAsync(state, baseResponse, utteranceWithContext);
                    state.LastSpeakerId = speakerResult.SpeakerId;

                    // 6. SHIP FINAL RESPONSE (Main user utterance)
                    await _responseService.SendResponseAsync(connectionId, state.SessionId!, speakerResult.SpeakerId, utteranceWithContext, baseResponse, speakerResult);
                        
                    // 7. SHIP ADDITIONAL RESPONSE (AI Assistance - Offset by 100ms)
                    if (intentStr == "AI_ASSISTANCE" && agent2Data.AIAssistance != null)
                    {
                        string aiAssistanceResponseTranslated = !string.IsNullOrWhiteSpace(agent2Data.AIAssistance.ResponseTranslated) 
                            ? agent2Data.AIAssistance.ResponseTranslated 
                            : agent2Data.AIAssistance.Response ?? mntTranslation;

                        var aiResponseForUI = new EnhancedTranslationResponse
                        {
                            ImprovedTranscription = agent2Data.AIAssistance.Response ?? "",
                            Translation = aiAssistanceResponseTranslated,
                            Intent = "AI_ASSISTANCE",
                            TranslationLanguage = baseResponse.TranslationLanguage,
                            AudioLanguage = baseResponse.AudioLanguage,
                            AIAssistance = agent2Data.AIAssistance, // Crucial for ConversationResponseService to handle AI audio correctly
                            EstimatedGender = "Unknown",
                            Confidence = 1.0f
                        };

                        // Add 100ms to the CreatedAt to ensure ordered display
                        var modifiedUtterance = new UtteranceWithContext
                        {
                            Text = utteranceWithContext.Text,
                            SourceLanguage = utteranceWithContext.SourceLanguage,
                            TargetLanguage = utteranceWithContext.TargetLanguage,
                            CreatedAt = utteranceWithContext.CreatedAt.AddMilliseconds(150), // Offset
                            TranscriptionConfidence = 1.0f
                        };

                        // The speaker is "Smart Tolk" or "AI Assistant", we can use a hardcoded AI ID
                        await _responseService.SendResponseAsync(connectionId, state.SessionId!, "assistant", modifiedUtterance, aiResponseForUI, new SpeakerOperationResult { SpeakerId = "assistant", DisplayName = "Smart Tolk" });
                    }

                    state.ResponseSentTime = DateTime.UtcNow;

                    // 7. BACKGROUND TASKS
                    _ = LogCycleMetricsAsync(connectionId, state, baseResponse, utteranceWithContext);

                    _logger.LogInformation("✅ {TurnId}: Utterance cycle UI delivered securely for {ConnectionId}", turnId, connectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ {TurnId}: Error in detached Agent 2 processing: {Message}", turnId, ex.Message);
                }
                finally
                {
                    try
                    {
                        // 8. UNBLOCK: If intent was AI_ASSISTANCE, Agent 2 is completely finished. Now allow next cycle.
                        // For SIMPLE_TRANSLATION, we already unblocked earlier.
                        if (!cycleUnblocked)
                        {
                            await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
                            state.ResetCycle();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ {TurnId}: Error unlocking cycle. {Message}", turnId, ex.Message);
                    }
                }
            };

        // 6. ASYNC EXECUTION SPLIT
        _logger.LogInformation("🚀 Launching Agent 2 processor in background.");
        _ = Task.Run(async () => {
            try {
                await processAgent2BackgroundAsync();
            } catch (Exception ex) {
                _logger.LogError(ex, "Background Agent 2 task failed.");
            }
        });

    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ {TurnId}: Error finalising utterance for {ConnectionId}", turnId, connectionId);
    }
}

private string CleanJsonStructure(string fullJson)
    {
        if (string.IsNullOrWhiteSpace(fullJson)) return "{}";
        var cleanedJson = fullJson.Trim();
        if (cleanedJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) cleanedJson = cleanedJson.Substring(7);
        else if (cleanedJson.StartsWith("```")) cleanedJson = cleanedJson.Substring(3);
        if (cleanedJson.EndsWith("```")) cleanedJson = cleanedJson.Substring(0, cleanedJson.Length - 3);
        return cleanedJson.Trim();
    }

    private async Task<SpeakerOperationResult> SyncSpeakerAndFactsAsync(
        ConversationState state, 
        A3ITranslator.Application.DTOs.Translation.EnhancedTranslationResponse brainResponse, 
        UtteranceWithContext utteranceWithContext)
    {
        var speakerPayload = new Application.DTOs.Translation.SpeakerServicePayload
        {
            TurnAnalysis = brainResponse.TurnAnalysis,
            Roster = brainResponse.SessionRoster,
            AudioLanguage = brainResponse.AudioLanguage,
            TranscriptionConfidence = utteranceWithContext.TranscriptionConfidence,
            AudioFingerprint = utteranceWithContext.AudioFingerprint
        };

        var speakerResult = await _speakerSyncService.IdentifySpeakerAfterUtteranceAsync(state.SessionId!, speakerPayload);
        state.LastSpeakerId = speakerResult.SpeakerId;
        
        return speakerResult;
    }


    private async Task UpdateRollingSummaryIfNeededAsync(ConversationState state)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(state.SessionId!, CancellationToken.None);
            if (session == null) return;

            // Rolling summaries removed - not needed for 3-4 hour meetings
            // Summary generation happens on-demand only via RequestSummaryAsync
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed in perpetual summary task");
        }
    }

    private async Task LogCycleMetricsAsync(string connectionId, ConversationState state, A3ITranslator.Application.DTOs.Translation.EnhancedTranslationResponse genAIResponse, UtteranceWithContext utteranceWithContext)
    {
        try
        {
            var sttRatePerSec = 0.000277;
            var sttCost = state.AccumulatedAudioSec * 2 * sttRatePerSec;
            var genAICost = (genAIResponse.Usage?.InputTokens * 0.00000015 ?? 0) + (genAIResponse.Usage?.OutputTokens * 0.0000006 ?? 0);
            var ttsCost = (genAIResponse.Translation?.Length ?? 0) * 0.000015;
            
            var cycleMetrics = new CycleMetrics
            {
                SessionId = state.SessionId ?? "unknown",
                ConnectionId = connectionId,
                CycleStartTime = state.CycleStartTime ?? DateTime.UtcNow,
                VADTriggerTime = state.VADTriggerTime,
                GenAIStartTime = state.GenAIStartTime,
                GenAIEndTime = state.GenAIEndTime,
                CycleEndTime = state.ResponseSentTime,
                ConversationItemSentTime = state.ResponseSentTime,
                AudioDurationSec = state.AccumulatedAudioSec,
                STTCost = sttCost,
                GenAICost = genAICost,
                TTSCost = ttsCost,
                TotalCost = sttCost + genAICost + ttsCost,
                GenAILatencyMs = (long)((state.GenAIEndTime - state.GenAIStartTime)?.TotalMilliseconds ?? 0),
                ImprovedTranscription = genAIResponse.ImprovedTranscription ?? utteranceWithContext.Text,
                Translation = genAIResponse.Translation ?? ""
            };
            
            await _metricsService.LogCycleMetricsAsync(cycleMetrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log cycle metrics");
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
        
        await _lifecycleManager.CleanupConnectionAsync(connectionId);
    }

    public async Task RequestSummaryAsync(string connectionId)
    {
        await _lifecycleManager.RequestSummaryAsync(connectionId);
    }

    public async Task FinalizeAndMailAsync(string connectionId, List<string> emailAddresses)
    {
        await _lifecycleManager.FinalizeAndMailAsync(connectionId, emailAddresses);
    }
}

/// <summary>
/// 🎯 UTTERANCE MANAGER
/// Manages transcription results, interim text, and utterance completion state
/// Centralizes all utterance-related logic for better separation of concerns
/// </summary>
[DebuggerDisplay("Completed={IsUtteranceCompleted}, Text={GetAccumulatedText()}")]
public class UtteranceManager
{
    public List<string> FinalUtterances { get; } = new();
    public List<TranscriptionResult> AllResults { get; } = new();
    public List<float> ConfidenceScores { get; } = new();
    public string CurrentInterimText { get; set; } = string.Empty;
    public bool IsUtteranceCompleted { get; set; } = false;
    
    private readonly string _connectionId;
    
    public UtteranceManager(string connectionId)
    {
        _connectionId = connectionId;
    }
    
    /// <summary>
    /// Check if utterance has accumulated text for processing
    /// </summary>
    public bool HasAccumulatedText 
    { 
        get 
        { 
            var hasText = FinalUtterances.Any();
            if (!hasText)
            {
                Console.WriteLine($"TIMESTAMP_HAS_ACCUMULATED_TEXT_FALSE: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - HasAccumulatedText=FALSE, FinalUtterances.Count={FinalUtterances.Count}, InterimText='{CurrentInterimText}'");
            }
            else
            {
                Console.WriteLine($"TIMESTAMP_HAS_ACCUMULATED_TEXT_TRUE: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - HasAccumulatedText=TRUE, FinalUtterances='{string.Join(" ", FinalUtterances)}'");
            }
            return hasText;
        } 
    }
    
    /// <summary>
    /// Add transcription result from STT service
    /// </summary>
    public void AddTranscriptionResult(TranscriptionResult result)
    {
        Console.WriteLine($"TIMESTAMP_TRANSCRIPTION_RECEIVED: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - STT result received: '{result.Text}' IsFinal: {result.IsFinal}");
        
        if (IsUtteranceCompleted)
        {
            Console.WriteLine($"TIMESTAMP_TRANSCRIPTION_IGNORED: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Ignoring STT result as utterance already completed");
            return; // Ignore results after completion
        }

        AllResults.Add(result);
        ConfidenceScores.Add((float)result.Confidence);

        if (result.IsFinal)
        {
            Console.WriteLine($"TIMESTAMP_FINAL_RESULT_PROCESSING: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Processing final STT result");
            ProcessFinalResult(result);
        }
        else
        {
            Console.WriteLine($"TIMESTAMP_INTERIM_RESULT_PROCESSING: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Processing interim STT result");
            UpdateInterimResult(result);
        }
    }
    
    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// </summary>
    public void CompleteUtterance()
    {
        Console.WriteLine($"TIMESTAMP_COMPLETE_UTTERANCE_CALLED: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - CompleteUtterance() called by frontend VAD");
        
        IsUtteranceCompleted = true;
        
        // Add any pending interim text as final
        if (!string.IsNullOrWhiteSpace(CurrentInterimText))
        {
            Console.WriteLine($"TIMESTAMP_INTERIM_TO_FINAL: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Converting interim text to final: '{CurrentInterimText}'");
            FinalUtterances.Add(CurrentInterimText.Trim());
            CurrentInterimText = string.Empty;
        }
        
        Console.WriteLine($"TIMESTAMP_COMPLETE_UTTERANCE_FINISHED: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - CompleteUtterance() finished, final utterances: {FinalUtterances.Count}");
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
    /// Get accumulated text from final utterances
    /// </summary>
    public string GetAccumulatedText()
    {
        return string.Join(" ", FinalUtterances).Trim();
    }
    
    /// <summary>
    /// Calculate average confidence from all results
    /// </summary>
    public float CalculateAverageConfidence()
    {
        if (ConfidenceScores.Count == 0) return 0f;
        return ConfidenceScores.Average();
    }
    

    
    /// <summary>
    /// Reset utterance state for new conversation cycle
    /// </summary>
    public void ResetUtteranceState()
    {
        FinalUtterances.Clear();
        CurrentInterimText = string.Empty;
        AllResults.Clear();
        ConfidenceScores.Clear();
        IsUtteranceCompleted = false;
    }
    
    /// <summary>
    /// Process final transcription result
    /// </summary>
    public void ProcessFinalResult(TranscriptionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            FinalUtterances.Add(result.Text.Trim());
        }
        CurrentInterimText = string.Empty;
    }
    
    /// <summary>
    /// Calculate total duration of all final utterances
    /// </summary>
    public double GetTotalDurationSeconds()
    {
        return AllResults.Where(r => r.IsFinal).Sum(r => r.Duration.TotalSeconds);
    }
    
    /// <summary>
    /// Update interim transcription result
    /// </summary>
    public void UpdateInterimResult(TranscriptionResult result)
    {
        CurrentInterimText = result.Text ?? string.Empty;
    }
}


    

    

    


[DebuggerDisplay("State={CycleState}, Lang={ProcessingLanguage}, Text={GetCurrentDisplayText()}")]
public class ConversationState : IDisposable
{
    public string ConnectionId { get; }
    public Channel<byte[]> AudioStreamChannel { get; private set; } = Channel.CreateUnbounded<byte[]>();
    public ConversationPhase CycleState { get; set; } = ConversationPhase.Ready;
    public string? SessionId { get; private set; }
    public string? PrimaryLanguage { get; private set; }
    public string? SecondaryLanguage { get; private set; }
    public string? LastSpeakerId { get; set; }
    
    // ⚡ SINGLE LANGUAGE: Processing language for this session
    public string ProcessingLanguage { get; set; } = "en-US";
    
    // ⚡ IMMEDIATE CANCELLATION: Store cancellation token for instant STT stopping
    public CancellationTokenSource? CurrentCycleCts { get; set; }
    
    // ⚡ DOUBLE-PROCESSING PREVENTION: Track if utterance is already being processed
    public bool IsProcessingStarted { get; set; } = false;
    
    // 🎯 UTTERANCE MANAGEMENT: Dedicated manager for utterance collection and processing
    private readonly UtteranceManager _utteranceManager;
    private readonly ConcurrentQueue<byte[]> _pendingAudioChunks = new();

    public UtteranceManager UtteranceManager => _utteranceManager;
    public byte[]? AudioHeader { get; set; }
    public string? ProvisionalSpeakerId { get; set; }
    public string? ProvisionalDisplayName { get; set; }
    public float SpeakerMatchConfidence { get; set; } = 0f;

    public void BufferPendingChunk(byte[] chunk) => _pendingAudioChunks.Enqueue(chunk);
    public bool TryDequeuePendingChunk(out byte[] chunk) => _pendingAudioChunks.TryDequeue(out chunk);
    public AudioFingerprint? AccumulatedAudioFingerprint { get; set; }
    
    // 📊 CYCLE METRICS TRACKING
    public DateTime? CycleStartTime { get; set; }
    public DateTime? VADTriggerTime { get; set; }
    public DateTime? GenAIStartTime { get; set; }
    public DateTime? GenAIEndTime { get; set; }
    public DateTime? ResponseSentTime { get; set; }
    public double AccumulatedAudioSec { get; set; } = 0;
    
    // STRICT DESIGN: Only accept audio when Ready (new cycle) or actively receiving (before VAD timeout)
    // Once VAD triggers ProcessingUtterance/SendingResponse, reject ALL new chunks until cycle completes
    public bool CanAcceptAudio => CycleState == ConversationPhase.Ready || CycleState == ConversationPhase.ReceivingAudio;
    
    public bool ShouldStartNewCycle => CycleState == ConversationPhase.Ready;
    
    /// <summary>
    /// 🎯 CRITICAL PROPERTY: Returns true when we have accumulated text for processing
    /// This drives the entire processing pipeline - must be true for utterance processing to begin
    /// </summary>
    public bool HasAccumulatedText => _utteranceManager.HasAccumulatedText;
    
    /// <summary>
    /// Expose IsUtteranceCompleted from UtteranceManager
    /// </summary>
    public bool IsUtteranceCompleted => _utteranceManager.IsUtteranceCompleted;
    
    public ConversationState(string connectionId) 
    {
        ConnectionId = connectionId;
        _utteranceManager = new UtteranceManager(connectionId);
    }

    public void CacheSessionConfig(string sessionId, string primary, string secondary)
    {
        SessionId = sessionId;
        PrimaryLanguage = primary;
        SecondaryLanguage = secondary;
        
        // Set processing language to primary language
        ProcessingLanguage = primary;
    }

    /// <summary>
    /// Add transcription result from STT service - delegate to UtteranceManager
    /// </summary>
    public void AddTranscriptionResult(TranscriptionResult result)
    {
        _utteranceManager.AddTranscriptionResult(result);
    }

    /// <summary>
    /// Signal utterance completion from frontend VAD - delegate to UtteranceManager
    /// </summary>
    public void CompleteUtterance()
    {
        // 📊 TRACK VAD TRIGGER
        VADTriggerTime = DateTime.UtcNow;
        
        _utteranceManager.CompleteUtterance();
    }

    /// <summary>
    /// Get current display text for real-time UI updates - delegate to UtteranceManager
    /// </summary>
    public string GetCurrentDisplayText()
    {
        return _utteranceManager.GetCurrentDisplayText();
    }

    /// <summary>
    /// Get complete utterance with resolved languages and speaker context
    /// </summary>
    public UtteranceWithContext GetCompleteUtterance()
    {
        Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_START: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - GetCompleteUtterance() started");
        
        if (!IsUtteranceCompleted || !_utteranceManager.FinalUtterances.Any())
        {
            Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Utterance not ready: Completed={IsUtteranceCompleted}, HasFinal={_utteranceManager.FinalUtterances.Any()}");
            throw new InvalidOperationException("Utterance not completed or no content available");
        }

        Console.WriteLine($"TIMESTAMP_LANGUAGE_RESOLUTION_START: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Starting language resolution");
        
        var dominantLanguage = ProcessingLanguage; // Use the processing language directly
        var (sourceLanguage, targetLanguage) = ResolveSourceTargetLanguages(dominantLanguage);
        var averageConfidence = _utteranceManager.CalculateAverageConfidence();

        var result = new UtteranceWithContext
        {
            Text = _utteranceManager.GetAccumulatedText(),
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            DominantLanguage = dominantLanguage,
            TranscriptionConfidence = averageConfidence,
            ProvisionalSpeakerId = ProvisionalSpeakerId,
            SpeakerConfidence = SpeakerMatchConfidence,
            DetectionResults = _utteranceManager.AllResults.ToList(),
            AudioFingerprint = AccumulatedAudioFingerprint,
            CreatedAt = DateTime.UtcNow
        };
        
        Console.WriteLine($"TIMESTAMP_GET_COMPLETE_UTTERANCE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - GetCompleteUtterance() complete: '{result.Text}' {result.SourceLanguage}→{result.TargetLanguage}");
        return result;
    }

    /// <summary>
    /// Set speaker identification context
    /// </summary>
    public void SetSpeakerContext(string? speakerId, string? displayName, float confidence, AudioFingerprint? fingerprint = null)
    {
        ProvisionalSpeakerId = speakerId;
        ProvisionalDisplayName = displayName;
        SpeakerMatchConfidence = confidence;
        AccumulatedAudioFingerprint = fingerprint;
    }


    public void ResetUtteranceState()
    {
        _utteranceManager.ResetUtteranceState();
        ProvisionalSpeakerId = null;
        SpeakerMatchConfidence = 0f;
        AccumulatedAudioFingerprint = null;
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
        
        // Reset metrics
        CycleStartTime = null;
        VADTriggerTime = null;
        GenAIStartTime = null;
        GenAIEndTime = null;
        ResponseSentTime = null;
        AccumulatedAudioSec = 0;
    }
    
    /// <summary>
    /// Reset the audio channel for a new conversation cycle
    /// </summary>
    public void ResetAudioChannel()
    {
        // Close old channel if it was somehow still open
        AudioStreamChannel.Writer.TryComplete();
        // Create a fresh one
        AudioStreamChannel = Channel.CreateUnbounded<byte[]>();
        Console.WriteLine($"TIMESTAMP_AUDIO_CHANNEL_RESET: {DateTime.UtcNow:HH:mm:ss.fff} - {ConnectionId} - Audio channel re-initialized for new cycle");
    }

    public void Dispose() => AudioStreamChannel.Writer.TryComplete();

    private (string sourceLanguage, string targetLanguage) ResolveSourceTargetLanguages(string sourceLanguage)
    {
        // Simple rule: if source is primary, target is secondary, and vice versa
        var targetLanguage = sourceLanguage == PrimaryLanguage ? SecondaryLanguage : PrimaryLanguage;
        return (sourceLanguage, targetLanguage ?? "en-US");
    }
}
