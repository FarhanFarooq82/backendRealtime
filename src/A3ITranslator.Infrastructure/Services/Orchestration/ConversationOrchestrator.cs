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
    private readonly IMetricsService _metricsService;

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
        IAudioFeatureExtractor featureExtractor,
        IMetricsService metricsService)
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
        _metricsService = metricsService;
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
            
            // 🔄 REFRESH CHANNEL: Synchronously reset for immediate writing
            state.ResetAudioChannel();
            
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
    /// ELEGANT APPROACH: Close stream and let STT finish naturally
    /// </summary>
    public async Task CompleteUtteranceAsync(string connectionId)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        _logger.LogInformation("🔇 Frontend VAD: Utterance completion signal received for {ConnectionId}", connectionId);
        
        Console.WriteLine($"TIMESTAMP_UTTERANCE_COMPLETION_SIGNAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Frontend VAD detected silence");
        
        // Mark utterance as completed (stops winner monitoring loop)
        state.CompleteUtterance();
        
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
        
        _logger.LogInformation("🛑 Frontend CANCEL: CancelUtterance signal received for {ConnectionId}", connectionId);
        Console.WriteLine($"TIMESTAMP_CANCEL_UTTERANCE_SIGNAL: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Frontend requested cancellation");

        // 1. Immediately cancel any active processing in this cycle
        state.CancelCurrentCycle();
        
        // 2. Clear buffers and reset state for immediate next use
        state.ResetCycle();
        
        // 3. Complete the audio channel to stop any pending reads
        state.AudioStreamChannel.Writer.TryComplete();
        
        // 4. Notify frontend that we are ready again
        await _notificationService.NotifyCycleCompletionAsync(connectionId, true);
        
        _logger.LogDebug("✅ Conversation cycle cancelled and reset for {ConnectionId}", connectionId);
        
        await Task.CompletedTask;
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
                rollingFingerprint, // Pass accumulator
                connectionId, 
                cycleCts.Token);
            
            Console.WriteLine($"TASK_START_ProcessDualSTTWithUtteranceManagersAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting STT task");
            var sttTask = ProcessDualSTTWithUtteranceManagersAsync(sttPrimaryChannel.Reader, sttSecondaryChannel.Reader, state, connectionId, cycleCts.Token);

            _logger.LogDebug("📡 ORCHESTRATOR: Started broadcaster and consumer tasks for {ConnectionId}", connectionId);

            // 3. ⚡ SMART WAITING: Wait for STT completion OR utterance completion
            Console.WriteLine($"TASK_WAIT_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting for STT completion or utterance completion");
            
            // Create a completion task that triggers when utterance is marked complete
            var utteranceCompletionTask = Task.Run(async () =>
            {
                while (!cycleCts.Token.IsCancellationRequested && !state.IsUtteranceCompleted)
                {
                    await Task.Delay(50, cycleCts.Token); // Fast polling for utterance completion
                }
                Console.WriteLine($"TASK_WAIT_END: {DateTime.UtcNow:HH:mm:ss.fff} - Utterance completion detected");
            });

            // Wait for EITHER STT task completion OR utterance completion
            await Task.WhenAny(sttTask, utteranceCompletionTask);
            Console.WriteLine($"TASK_WAIT_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT task or utterance completed");

            // If utterance was completed by frontend VAD, catch lingering chunks then close
            if (state.IsUtteranceCompleted)
            {
                // 🎨 GRACE PERIOD: Wait 500ms for any late chunks from the network
                Console.WriteLine($"TIMESTAMP_GRACE_PERIOD_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting 500ms for lingering chunks");
                await Task.Delay(500);
                
                // Now safely close the channel
                state.AudioStreamChannel.Writer.TryComplete();
                Console.WriteLine($"TIMESTAMP_STREAM_CLOSED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Audio stream closed after grace period");
                
                Console.WriteLine($"TIMESTAMP_WAITING_FOR_WINNER_ADOPTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting for winner adoption to complete");
                
                // Wait longer (up to 3 seconds) for the full STT cloud response to arrive
                var waitTask = Task.WhenAny(sttTask, Task.Delay(3000, cycleCts.Token));
                await waitTask;
                
                Console.WriteLine($"TIMESTAMP_WINNER_ADOPTION_WAIT_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Winner adoption wait complete, HasAccumulatedText: {state.HasAccumulatedText}");
            }

            // Cancel remaining tasks
            if (!cycleCts.IsCancellationRequested) cycleCts.Cancel();

            // 4. ⚡ PROCESSING OPTIMIZATION: Process immediately if we have valid utterance data
            if (state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                _logger.LogInformation("🎯 Frontend VAD completion detected - processing utterance immediately for {ConnectionId}", connectionId);
                await ProcessUtteranceWithTransition(connectionId, state, "Frontend VAD Signal", rollingFingerprint);
            }
            else if (!state.IsUtteranceCompleted && state.HasAccumulatedText && !state.IsProcessingStarted)
            {
                state.IsProcessingStarted = true; // Prevent double processing
                _logger.LogInformation("🔄 STT channel closed with text but no frontend signal - auto-completing utterance for {ConnectionId}", connectionId);
                state.CompleteUtterance(); // Mark as completed
                await ProcessUtteranceWithTransition(connectionId, state, "Auto-completion (STT ended)", rollingFingerprint);
            }
            else
            {
                _logger.LogDebug("✅ No accumulated text to process for {ConnectionId}", connectionId);
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
        
        // Log STT Metrics
        var audioSec = state.UtteranceManager.GetTotalDurationSeconds();
        _ = _metricsService.LogMetricsAsync(new UsageMetrics
        {
            SessionId = state.SessionId ?? "unknown",
            ConnectionId = connectionId,
            Category = ServiceCategory.STT,
            Provider = "Azure", // Fixed as we use Azure currently
            Operation = "StreamingTranscription",
            InputUnits = (long)audioSec,
            InputUnitType = "Seconds",
            AudioLengthSec = audioSec,
            UserPrompt = "AUDIO_STREAM",
            Response = state.UtteranceManager.GetAccumulatedText(),
            CostUSD = audioSec * 0.000266, // $0.016/min
            Status = "Success"
        });

        // Pass the accumulated fingerprint to the processing logic
        state.SetSpeakerContext(null, 0, rollingFingerprint);
        
        await ProcessCompletedUtteranceAsync(connectionId, state);
    }

    /// <summary>
    /// Broadcast audio to dual STT processing channels for language competition
    /// </summary>
    private async Task BroadcastAudioToDualChannelsAsync(
        ChannelReader<byte[]> audioReader, 
        Channel<byte[]> primaryChannel,
        Channel<byte[]> secondaryChannel,
        AudioFingerprint fingerprintAccumulator,
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
                    
                    // ✨ Background Feature Extraction (Zero Latency)
                    _ = _featureExtractor.AccumulateFeaturesAsync(chunk, fingerprintAccumulator);
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
    /// 🎯 DUAL UTTERANCE MANAGERS: Process both primary and secondary languages in parallel
    /// Winner selection based on confidence, loser gets stopped immediately
    /// </summary>
    private async Task ProcessDualSTTWithUtteranceManagersAsync(
        ChannelReader<byte[]> primaryAudioReader,
        ChannelReader<byte[]> secondaryAudioReader,
        ConversationState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"TASK_START_ProcessDualSTTWithUtteranceManagersAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting dual STT processing for Primary: {state.PrimaryLanguage}, Secondary: {state.SecondaryLanguage}");
            
            // 🎯 CREATE DUAL UTTERANCE MANAGERS
            var primaryUtteranceManager = new LanguageSpecificUtteranceManager(state.PrimaryLanguage!, "Primary");
            var secondaryUtteranceManager = new LanguageSpecificUtteranceManager(state.SecondaryLanguage!, "Secondary");
            
            // 🎯 DUAL STT PROCESSING: Both languages compete in parallel
            var primaryTask = ProcessSingleLanguageSTTAsync(primaryAudioReader, state.PrimaryLanguage!, primaryUtteranceManager, connectionId, cancellationToken);
            var secondaryTask = ProcessSingleLanguageSTTAsync(secondaryAudioReader, state.SecondaryLanguage!, secondaryUtteranceManager, connectionId, cancellationToken);
            
            Console.WriteLine($"TIMESTAMP_DUAL_STT_STARTED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Both STT streams started, monitoring for winner");
            
            // 🎯 WINNER MONITORING: Wait for first significant result with high confidence
            var winnerSelected = false;
            var loserCts = new CancellationTokenSource();
            Task? winnerTask = null; // Track which task won
            
            // 🚀 PARALLEL TASK EXECUTION: Start both STT tasks and monitor for winner
            var winnerMonitoringTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && !winnerSelected)
                {
                    // Check if utterance was completed by frontend VAD
                    if (state.IsUtteranceCompleted && !winnerSelected)
                    {
                        // 🔥 CRITICAL: Do ONE FINAL winner check before exiting
                        Console.WriteLine($"TIMESTAMP_FINAL_WINNER_CHECK: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Utterance completed, doing final winner evaluation");
                        
                        var primaryFinal = primaryUtteranceManager.IsWinnerCandidate();
                        var secondaryFinal = secondaryUtteranceManager.IsWinnerCandidate();
                        
                        if (primaryFinal || secondaryFinal)
                        {
                            var winner = primaryFinal && secondaryFinal ? 
                                (primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() ? primaryUtteranceManager : secondaryUtteranceManager) :
                                (primaryFinal ? primaryUtteranceManager : secondaryUtteranceManager);
                            
                            // Track which task is the winner
                            winnerTask = winner == primaryUtteranceManager ? primaryTask : secondaryTask;
                            
                            Console.WriteLine($"TIMESTAMP_FINAL_WINNER_SELECTED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - FINAL WINNER: {winner.LanguageCode} with confidence {winner.GetConfidence():F2}, text: '{winner.GetBestText()}'");
                            state.AdoptWinnerResults(winner);
                            winnerSelected = true;
                        }
                        else
                        {
                            // No winner yet, try to pick the best manager we have
                            var bestManager = primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() 
                                ? primaryUtteranceManager 
                                : secondaryUtteranceManager;
                            
                            if (bestManager.GetAllResults().Any())
                            {
                                // Track which task is the winner
                                winnerTask = bestManager == primaryUtteranceManager ? primaryTask : secondaryTask;
                                
                                Console.WriteLine($"TIMESTAMP_FALLBACK_ADOPTION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - No clear winner, adopting best: {bestManager.LanguageCode}");
                                state.AdoptWinnerResults(bestManager);
                                winnerSelected = true;
                            }
                        }
                        
                        break; // Exit monitoring after final check
                    }
                    
                    await Task.Delay(100, cancellationToken); // Fast polling for winner selection
                    
                    // 🏆 CHECK FOR WINNER: High confidence result with substantial text
                    var primaryWinner = primaryUtteranceManager.IsWinnerCandidate();
                    var secondaryWinner = secondaryUtteranceManager.IsWinnerCandidate();
                    
                    // 🎯 DEBUG: Log winner evaluation details
                    if (primaryWinner || secondaryWinner)
                    {
                        Console.WriteLine($"TIMESTAMP_WINNER_EVALUATION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Primary: IsWinner={primaryWinner}, Confidence={primaryUtteranceManager.GetConfidence():F2}, Text='{primaryUtteranceManager.GetBestText()}'");
                        Console.WriteLine($"TIMESTAMP_WINNER_EVALUATION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Secondary: IsWinner={secondaryWinner}, Confidence={secondaryUtteranceManager.GetConfidence():F2}, Text='{secondaryUtteranceManager.GetBestText()}'");
                    }
                    
                    if (primaryWinner || secondaryWinner)
                    {
                        var winner = primaryWinner && secondaryWinner ? 
                            (primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() ? primaryUtteranceManager : secondaryUtteranceManager) :
                            (primaryWinner ? primaryUtteranceManager : secondaryUtteranceManager);
                        
                        var loser = winner == primaryUtteranceManager ? secondaryUtteranceManager : primaryUtteranceManager;
                        
                        // Track which task is the winner
                        winnerTask = winner == primaryUtteranceManager ? primaryTask : secondaryTask;
                        
                        Console.WriteLine($"TIMESTAMP_WINNER_SELECTED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - WINNER: {winner.LanguageCode} ({winner.Name}) with confidence {winner.GetConfidence():F2}, text: '{winner.GetBestText()}'");
                        Console.WriteLine($"TIMESTAMP_LOSER_STOPPED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STOPPING: {loser.LanguageCode} ({loser.Name}) with confidence {loser.GetConfidence():F2}");
                        
                        // 🎯 WINNER SELECTION: Transfer winner's results to main state and stop loser
                        Console.WriteLine($"TIMESTAMP_BEFORE_ADOPT_WINNER: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Before adopting winner, state.HasAccumulatedText: {state.HasAccumulatedText}");
                        state.AdoptWinnerResults(winner);
                        Console.WriteLine($"TIMESTAMP_AFTER_ADOPT_WINNER: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - After adopting winner, state.HasAccumulatedText: {state.HasAccumulatedText}");
                        
                        winnerSelected = true;
                        
                        // Stop the losing STT stream immediately
                        loserCts.Cancel();
                        
                        // Continue monitoring only the winner for additional results
                        await MonitorWinnerSTTAsync(winner, state, connectionId, cancellationToken);
                        break;
                    }
                }
            });

            // 🎯 WINNER-FOCUSED TASK MANAGEMENT: Wait for completion and ensure results are processed
            try
            {
                // Wait for winner selection OR any task completion
                var firstCompleted = await Task.WhenAny(primaryTask, secondaryTask, winnerMonitoringTask);
                
                // If VAD triggered (IsUtteranceCompleted) but no winner yet, we MUST wait for STT tasks to finish
                if (state.IsUtteranceCompleted && !winnerSelected)
                {
                    Console.WriteLine($"TIMESTAMP_VAD_DRAINING: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Utterance completed, draining STT tasks for final results");
                    
                    // Wait for both STT tasks to finish naturally (since stream is closed)
                    // We give them a max of 2 seconds to get the final results from cloud
                    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await Task.WhenAll(primaryTask, secondaryTask);
                    
                    Console.WriteLine($"TIMESTAMP_STT_DRAINED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - STT tasks drained, performing last-second winner check");
                    
                    // 🎯 FINAL FALLBACK: If no winner was selected during live monitoring, pick the best one now
                    if (!winnerSelected)
                    {
                        var primaryBest = primaryUtteranceManager.IsWinnerCandidate();
                        var secondaryBest = secondaryUtteranceManager.IsWinnerCandidate();
                        
                        var fallbackWinner = primaryBest && secondaryBest ? 
                            (primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() ? primaryUtteranceManager : secondaryUtteranceManager) :
                            (primaryBest ? primaryUtteranceManager : (secondaryBest ? secondaryUtteranceManager : null));
                        
                        // If still no "candidate" winner, just pick the one with better results
                        if (fallbackWinner == null)
                        {
                            fallbackWinner = (primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence())
                                ? primaryUtteranceManager
                                : secondaryUtteranceManager;
                        }

                        if (fallbackWinner.GetAllResults().Any())
                        {
                            Console.WriteLine($"TIMESTAMP_FALLBACK_WINNER_SELECTED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Picked fallback winner: {fallbackWinner.LanguageCode}");
                            state.AdoptWinnerResults(fallbackWinner);
                            winnerSelected = true;
                        }
                    }
                }
                
                // If a winner was already selected during the loop, wait for that specific winner to finish
                if (winnerSelected && winnerTask != null)
                {
                    Console.WriteLine($"TIMESTAMP_WAITING_FOR_WINNER_COMPLETION: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Waiting for winner task to finalize");
                    await winnerTask;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when frontend VAD triggers or cycle ends
                loserCts.Cancel(); // Ensure loser is stopped
            }
            
            Console.WriteLine($"TIMESTAMP_DUAL_STT_MONITORING_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual STT monitoring completed, WinnerSelected={winnerSelected}");
        }
        catch (OperationCanceledException) 
        { 
            Console.WriteLine($"TASK_CANCELLED_ProcessDualSTTWithUtteranceManagersAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual STT task cancelled (expected on utterance completion)");
            _logger.LogDebug("🔇 Dual STT processing cancelled for {ConnectionId} (expected behavior)", connectionId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TASK_ERROR_ProcessDualSTTWithUtteranceManagersAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Dual STT error: {ex.Message}");
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(ex, "❌ Dual STT processing error for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Process STT for a single language and feed results to utterance manager
    /// </summary>
    private async Task ProcessSingleLanguageSTTAsync(
        ChannelReader<byte[]> audioReader,
        string language,
        LanguageSpecificUtteranceManager utteranceManager,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"TASK_START_ProcessSingleLanguageSTTAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Starting {utteranceManager.Name} STT for {language}");
            
            Console.WriteLine($"TIMESTAMP_FOREACH_START_{utteranceManager.Name.ToUpper()}: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name} starting to consume STT results");
            
            await foreach (var result in _sttService.ProcessStreamAsync(audioReader, language, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"TIMESTAMP_STT_CANCELLED: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name} STT cancelled");
                    break;
                }
                
                Console.WriteLine($"TIMESTAMP_STT_RESULT_{utteranceManager.Name.ToUpper()}: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name}: '{result.Text}' IsFinal: {result.IsFinal}, Confidence: {result.Confidence:F2}");
                
                // Feed result to language-specific utterance manager
                utteranceManager.AddResult(result);
            }
            
            Console.WriteLine($"TIMESTAMP_FOREACH_END_{utteranceManager.Name.ToUpper()}: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name} finished consuming STT results");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"TASK_CANCELLED_ProcessSingleLanguageSTTAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name} STT cancelled (expected when loser)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TASK_ERROR_ProcessSingleLanguageSTTAsync: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - {utteranceManager.Name} STT error: {ex.Message}");
            _logger.LogError(ex, "❌ {LanguageName} STT error for {ConnectionId}", utteranceManager.Name, connectionId);
        }
    }

    /// <summary>
    /// Continue monitoring the winning STT stream until completion
    /// </summary>
    private async Task MonitorWinnerSTTAsync(
        LanguageSpecificUtteranceManager winner,
        ConversationState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"TIMESTAMP_WINNER_MONITORING_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Monitoring winner {winner.Name} for continued results");
            
            while (!cancellationToken.IsCancellationRequested && !state.IsUtteranceCompleted)
            {
                await Task.Delay(50, cancellationToken); // Fast polling
                
                // Check for new results from winner and sync to state
                if (winner.HasNewResults())
                {
                    var newResults = winner.GetAndClearNewResults();
                    Console.WriteLine($"TIMESTAMP_WINNER_NEW_RESULTS: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Winner has {newResults.Count} new results to sync");
                    
                    foreach (var result in newResults)
                    {
                        Console.WriteLine($"TIMESTAMP_WINNER_SYNC_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Syncing result: '{result.Text}' IsFinal: {result.IsFinal}");
                        state.AddTranscriptionResult(result);
                        
                        // Send live updates to frontend
                        var displayText = state.GetCurrentDisplayText();
                        Console.WriteLine($"TIMESTAMP_WINNER_FRONTEND_UPDATE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Sending winner update: '{displayText}', HasAccumulatedText: {state.HasAccumulatedText}");
                        await _notificationService.NotifyTranscriptionAsync(connectionId, displayText, result.Language, false);
                    }
                }
            }
            
            Console.WriteLine($"TIMESTAMP_WINNER_MONITORING_END: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Winner monitoring completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Winner monitoring error for {ConnectionId}", connectionId);
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
                    1.0f // AI response assumed high confidence
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

                // Log TTS Metrics (Neural)
                _ = _metricsService.LogMetricsAsync(new UsageMetrics
                {
                    SessionId = state?.SessionId ?? "unknown",
                    ConnectionId = connectionId,
                    Category = ServiceCategory.TTS,
                    Provider = "Azure",
                    Operation = "NeuralTTS",
                    OutputUnits = text.Length,
                    OutputUnitType = "Characters",
                    UserPrompt = text,
                    Response = "AUDIO_STREAM",
                    CostUSD = text.Length * 0.000016, // $16/1M characters
                    Status = "Success"
                });
            }
            else
            {
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_STANDARD_START: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Using Standard TTS for continuous streaming");
                // Fallback to standard TTS service with continuous streaming
                _logger.LogInformation("🔊 Starting continuous Standard TTS: {Text} (Language: {Language})", text, language);
                
                // Use the standard TTS service's continuous streaming
                await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language);
                
                Console.WriteLine($"TIMESTAMP_TTS_CONTINUOUS_STANDARD_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - {connectionId} - Continuous Standard TTS completed");

                // Log TTS Metrics (Standard)
                _ = _metricsService.LogMetricsAsync(new UsageMetrics
                {
                    SessionId = state?.SessionId ?? "unknown",
                    ConnectionId = connectionId,
                    Category = ServiceCategory.TTS,
                    Provider = "Azure",
                    Operation = "StandardTTS",
                    OutputUnits = text.Length,
                    OutputUnitType = "Characters",
                    SystemPrompt = null, // TTS does not use a system prompt
                    UserPrompt = text,
                    Response = "AUDIO_STREAM",
                    CostUSD = text.Length * 0.000004, // $4/1M characters
                    Status = "Success"
                });
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

    public async Task RequestSummaryAsync(string connectionId)
    {
        try
        {
            _logger.LogInformation("🚀 ORCHESTRATOR: Generating session summary for {ConnectionId}", connectionId);
            
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogWarning("⚠️ ORCHESTRATOR: Session not found for {ConnectionId}, cannot generate summary", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, "Session not found.");
                return;
            }

            // Build history string from conversation turns
            var historyBuilder = new StringBuilder();
            foreach (var turn in session.ConversationHistory.OrderBy(t => t.Timestamp))
            {
                historyBuilder.AppendLine($"[{turn.Timestamp:HH:mm:ss}] {turn.SpeakerName}: {turn.OriginalText}");
                if (!string.IsNullOrEmpty(turn.TranslatedText))
                {
                    historyBuilder.AppendLine($"   (Translation: {turn.TranslatedText})");
                }
            }
            
            var history = historyBuilder.ToString();
            
            if (string.IsNullOrWhiteSpace(history))
            {
                await _notificationService.SendSessionSummaryAsync(connectionId, "No conversation history available to summarize.");
                return;
            }

            var summary = await _translationOrchestrator.GenerateConversationSummaryAsync(
                history, 
                session.PrimaryLanguage, 
                session.SecondaryLanguage ?? "en-US");

            await _notificationService.SendSessionSummaryAsync(connectionId, summary);
            _logger.LogInformation("✅ ORCHESTRATOR: Summary sent for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ORCHESTRATOR: Failed to handle summary request for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, $"Summary generation failed: {ex.Message}");
        }
    }

    public async Task FinalizeAndMailAsync(string connectionId, List<string> emailAddresses)
    {
        try
        {
            _logger.LogInformation("🚀 ORCHESTRATOR: Finalizing session and mailing for {ConnectionId} to {Count} addresses", 
                connectionId, emailAddresses.Count);
            
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogWarning("⚠️ ORCHESTRATOR: Session not found for {ConnectionId}, cannot finalize", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, "Session not found.");
                return;
            }

            // 1. Generate PDF (Mock)
            // In a real implementation, we would use a library like QuestPDF or iText
            _logger.LogInformation("📄 MOCK: Generating PDF for connection {ConnectionId} with {TurnCount} turns", 
                connectionId, session.ConversationHistory.Count);
            
            // 2. Send Emails (Mock)
            foreach (var email in emailAddresses)
            {
                _logger.LogInformation("📧 MOCK: Sending transcript PDF to {Email}", email);
            }
            
            // 3. Notify success
            await _notificationService.SendFinalizationSuccessAsync(connectionId);
            
            _logger.LogInformation("✅ ORCHESTRATOR: Finalization successful for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ORCHESTRATOR: Failed to finalize session for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, $"Finalization failed: {ex.Message}");
        }
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

    // --- Required Interface Methods ---
    public Task<ConversationResult> ProcessTranscriptionAsync(string connectionId, string transcription, string detectedLanguage, float confidence, CancellationToken cancellationToken = default) 
        => Task.FromResult(new ConversationResult { Success = true });
    
    public Task<bool> ProcessGeneratedResponseAsync(string connectionId, ConversationItem conversationItem, CancellationToken cancellationToken = default) 
        => Task.FromResult(true);
    
    public Task CompleteConversationCycleAsync(string connectionId, string conversationItemId, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
    
    public Task HandleConversationErrorAsync(string connectionId, string error, ConversationErrorSeverity severity, CancellationToken cancellationToken = default) 
        => Task.CompletedTask;
}

/// <summary>
/// 🎯 UTTERANCE MANAGER
/// Manages transcription results, interim text, and utterance completion state
/// Centralizes all utterance-related logic for better separation of concerns
/// </summary>
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
    /// Adopt winner's results from language-specific utterance manager
    /// 🎯 CRITICAL: This method must properly transfer ALL winner's data including interim text
    /// </summary>
    public void AdoptWinnerResults(LanguageSpecificUtteranceManager winner)
    {
        Console.WriteLine($"TIMESTAMP_ADOPTING_WINNER: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Adopting results from {winner.Name} ({winner.LanguageCode})");
        
        // Clear existing results and adopt winner's
        FinalUtterances.Clear();
        AllResults.Clear();
        ConfidenceScores.Clear();
        CurrentInterimText = string.Empty;
        
        // Transfer all results from winner
        foreach (var result in winner.GetAllResults())
        {
            AllResults.Add(result);
            ConfidenceScores.Add((float)result.Confidence);
            
            if (result.IsFinal && !string.IsNullOrWhiteSpace(result.Text))
            {
                FinalUtterances.Add(result.Text.Trim());
            }
        }
        
        // 🚀 CRITICAL FIX: Get the best text from winner (final + interim combined)
        var winnerBestText = winner.GetBestText();
        Console.WriteLine($"TIMESTAMP_WINNER_BEST_TEXT: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Winner best text: '{winnerBestText}'");
        
        // 🚀 CRITICAL FIX: Set current interim text from winner
        CurrentInterimText = winner.GetCurrentInterim();
        
        // 🚀 CRITICAL FIX: If we have good text but no final utterances, promote best text to final
        // This ensures HasAccumulatedText returns true immediately after winner selection
        if (!FinalUtterances.Any() && !string.IsNullOrWhiteSpace(winnerBestText))
        {
            Console.WriteLine($"TIMESTAMP_PROMOTING_BEST_TEXT_TO_FINAL: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - No final utterances, promoting best text to final: '{winnerBestText}'");
            FinalUtterances.Add(winnerBestText.Trim());
            // Keep interim text for live updates
        }
        else if (FinalUtterances.Any() && !string.IsNullOrWhiteSpace(CurrentInterimText))
        {
            // If we have both final utterances AND interim text, add interim to final for completeness
            Console.WriteLine($"TIMESTAMP_ADDING_INTERIM_TO_FINALS: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Adding interim text to finals: '{CurrentInterimText}'");
            FinalUtterances.Add(CurrentInterimText.Trim());
        }
        
        Console.WriteLine($"TIMESTAMP_WINNER_ADOPTED: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - Adopted {AllResults.Count} results, {FinalUtterances.Count} final utterances from {winner.Name}");
        Console.WriteLine($"TIMESTAMP_WINNER_ADOPTED_TEXT: {DateTime.UtcNow:HH:mm:ss.fff} - {_connectionId} - HasAccumulatedText: {HasAccumulatedText}, FinalText: '{string.Join(" ", FinalUtterances)}', InterimText: '{CurrentInterimText}'");
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

/// <summary>
/// 🎯 LANGUAGE-SPECIFIC UTTERANCE MANAGER
/// Manages transcription results for a single language with confidence tracking
/// Used in dual-language competition for winner selection
/// </summary>
public class LanguageSpecificUtteranceManager
{
    public string LanguageCode { get; }
    public string Name { get; }
    
    private readonly List<TranscriptionResult> _allResults = new();
    private readonly List<TranscriptionResult> _newResults = new();
    private readonly List<string> _finalUtterances = new();
    private string _currentInterim = string.Empty;
    private readonly object _lock = new();
    
    // Winner selection criteria
    private const float WINNER_CONFIDENCE_THRESHOLD = 0.7f;
    private const int WINNER_MIN_TEXT_LENGTH = 10;
    
    public LanguageSpecificUtteranceManager(string languageCode, string name)
    {
        LanguageCode = languageCode;
        Name = name;
    }
    
    public void AddResult(TranscriptionResult result)
    {
        lock (_lock)
        {
            _allResults.Add(result);
            _newResults.Add(result);
            
            if (result.IsFinal && !string.IsNullOrWhiteSpace(result.Text))
            {
                _finalUtterances.Add(result.Text.Trim());
                _currentInterim = string.Empty;
            }
            else
            {
                _currentInterim = result.Text ?? string.Empty;
            }
        }
    }
    
    public bool IsWinnerCandidate()
    {
        lock (_lock)
        {
            var confidence = GetConfidence();
            var text = GetBestText();
            
            // 🚀 RELAXED CRITERIA: Winner can be selected with good interim results OR final results
            // We need either:
            // 1. Good final utterances with decent confidence
            // 2. OR good interim text with high confidence (for immediate selection)
            var hasFinalText = _finalUtterances.Any() && _finalUtterances.Sum(u => u.Length) >= WINNER_MIN_TEXT_LENGTH;
            var hasGoodInterim = !string.IsNullOrWhiteSpace(_currentInterim) && 
                                _currentInterim.Length >= WINNER_MIN_TEXT_LENGTH && 
                                confidence >= WINNER_CONFIDENCE_THRESHOLD + 0.1f; // Slightly higher threshold for interim
            
            return confidence >= WINNER_CONFIDENCE_THRESHOLD && 
                   text.Length >= WINNER_MIN_TEXT_LENGTH &&
                   (hasFinalText || hasGoodInterim);
        }
    }
    
    public float GetConfidence()
    {
        lock (_lock)
        {
            if (!_allResults.Any()) return 0f;
            return (float)_allResults.Average(r => r.Confidence);
        }
    }
    
    public string GetBestText()
    {
        lock (_lock)
        {
            var accumulated = string.Join(" ", _finalUtterances).Trim();
            if (!string.IsNullOrWhiteSpace(_currentInterim))
            {
                return string.IsNullOrWhiteSpace(accumulated) ? _currentInterim : $"{accumulated} {_currentInterim}";
            }
            return accumulated;
        }
    }
    
    public string GetCurrentInterim()
    {
        lock (_lock)
        {
            return _currentInterim;
        }
    }

    public double GetTotalDurationSeconds()
    {
        lock (_lock)
        {
            return _allResults.Where(r => r.IsFinal).Sum(r => r.Duration.TotalSeconds);
        }
    }
    
    public List<TranscriptionResult> GetAllResults()
    {
        lock (_lock)
        {
            return _allResults.ToList();
        }
    }
    
    public bool HasNewResults()
    {
        lock (_lock)
        {
            return _newResults.Any();
        }
    }
    
    public List<TranscriptionResult> GetAndClearNewResults()
    {
        lock (_lock)
        {
            var results = _newResults.ToList();
            _newResults.Clear();
            return results;
        }
    }
}

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
    public string ProcessingLanguage { get; private set; } = "en-US";
    
    // ⚡ IMMEDIATE CANCELLATION: Store cancellation token for instant STT stopping
    public CancellationTokenSource? CurrentCycleCts { get; set; }
    
    // ⚡ DOUBLE-PROCESSING PREVENTION: Track if utterance is already being processed
    public bool IsProcessingStarted { get; set; } = false;
    
    // 🎯 UTTERANCE MANAGEMENT: Dedicated manager for utterance collection and processing
    private readonly UtteranceManager _utteranceManager;
    public UtteranceManager UtteranceManager => _utteranceManager;
    public string? ProvisionalSpeakerId { get; set; }
    public float SpeakerMatchConfidence { get; set; } = 0f;
    public AudioFingerprint? AccumulatedAudioFingerprint { get; set; }
    
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
    public void SetSpeakerContext(string? speakerId, float confidence, AudioFingerprint? fingerprint = null)
    {
        ProvisionalSpeakerId = speakerId;
        SpeakerMatchConfidence = confidence;
        AccumulatedAudioFingerprint = fingerprint;
    }

    /// <summary>
    /// Adopt winner's results from language-specific utterance manager
    /// 🎯 CRITICAL: This method must properly transfer ALL winner's data including interim text
    /// </summary>
    public void AdoptWinnerResults(LanguageSpecificUtteranceManager winner)
    {
        _utteranceManager.AdoptWinnerResults(winner);
        // Update processing language to winner's language
        ProcessingLanguage = winner.LanguageCode;
    }
    public void ResetUtteranceState()
    {
        _utteranceManager.ResetUtteranceState();
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
