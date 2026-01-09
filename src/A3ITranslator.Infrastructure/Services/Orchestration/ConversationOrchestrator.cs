using A3ITranslator.Application.DTOs.Common;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Models.SpeakerProfiles;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.Enums;
using Microsoft.Extensions.Logging;
using A3ITranslator.Application.DTOs.Speaker;
using System.Threading.Channels;
using A3ITranslator.Application.Models;

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
    private readonly ISpeakerDecisionEngine _speakerDecisionEngine;
    private readonly ISpeakerProfileManager _speakerProfileManager;
    private readonly ITranslationOrchestrator _translationOrchestrator;

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
        ISpeakerDecisionEngine speakerDecisionEngine,
        ISpeakerProfileManager speakerProfileManager,
        ITranslationOrchestrator translationOrchestrator)
    {
        _logger = logger;
        _notificationService = notificationService;
        _ttsService = ttsService;
        _sessionRepository = sessionRepository;
        _sttService = sttService;
        _speakerService = speakerService;
        _speakerDecisionEngine = speakerDecisionEngine;
        _speakerProfileManager = speakerProfileManager;
        _translationOrchestrator = translationOrchestrator;
    }

    /// <summary>
    /// Main entry point: Process audio chunk with proper state management
    /// </summary>
    public async Task ProcessAudioChunkAsync(string connectionId, byte[] audioChunk)
    {
        var state = GetOrCreateConversationState(connectionId);
        
        // Check if we can accept audio in current state
        if (!state.CanAcceptAudio)
        {
            _logger.LogWarning("🚫 ORCHESTRATOR: Rejecting audio chunk for {ConnectionId} - state: {State}", 
                connectionId, state.CycleState);
            
            // Send signal to frontend to stop sending audio
            await _notificationService.NotifyProcessingStatusAsync(connectionId, 
                $"Processing in progress, please wait... (State: {state.CycleState})");
            return;
        }
        
        // Write to audio stream channel
        var writeResult = state.AudioStreamChannel.Writer.TryWrite(audioChunk);
        
        if (!writeResult)
        {
            _logger.LogWarning("❌ ORCHESTRATOR: Failed to write audio chunk to channel for {ConnectionId}", connectionId);
            return;
        }

        // Start processing pipeline if ready for new cycle
        if (state.ShouldStartNewCycle)
        {
            state.StartReceivingAudio();
            _ = Task.Run(() => ProcessConversationPipelineAsync(connectionId, state));
            
            _logger.LogDebug("🎤 ORCHESTRATOR: Started new cycle for {ConnectionId} - now receiving audio", connectionId);
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
            var sttChannel = Channel.CreateUnbounded<byte[]>();
            var speakerChannel = Channel.CreateUnbounded<byte[]>();

            // 2. Start parallel processing tasks
            // CRITICAL: Start broadcaster FIRST so audio flows to channels before consumers start
            var broadcasterTask = BroadcastAudioAsync(state.AudioStreamChannel.Reader, sttChannel, speakerChannel, connectionId, cycleCts.Token);
            
            // Small delay to ensure broadcaster has started reading before consumers start waiting
            await Task.Delay(10, cycleCts.Token);
            
            var sttTask = ProcessSTTWithSpeakerContextAsync(sttChannel.Reader, utteranceCollector, state.CandidateLanguages, connectionId, cycleCts.Token);
            var speakerTask = ProcessSpeakerIdentificationAsync(speakerChannel.Reader, utteranceCollector, connectionId, cycleCts.Token);

            _logger.LogDebug("📡 ORCHESTRATOR: Started broadcaster and consumer tasks for {ConnectionId}", connectionId);

            // 3. VAD MONITOR: Polling task to check for silence after speech
            var monitorTask = Task.Run(async () => 
            {
                while (!cycleCts.Token.IsCancellationRequested)
                {
                    // If we have text and have timed out (enough silence), stop the cycle
                    if (utteranceCollector.HasTimedOut() && utteranceCollector.HasAccumulatedText)
                    {
                        _logger.LogInformation("🔇 VAD: Silence detected for {ConnectionId}, ending cycle", connectionId);
                        cycleCts.Cancel();
                        break;
                    }
                    await Task.Delay(500, cycleCts.Token);
                }
            }, cycleCts.Token);

            // 4. Wait for ANY task to signal completion (VAD, STT end, or Error)
            // CRITICAL: Include broadcasterTask so audio actually flows to STT/speaker channels
            await Task.WhenAny(broadcasterTask, sttTask, speakerTask, monitorTask);

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
        Channel<byte[]> speakerChannel,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var chunkCount = 0;
        var totalBytes = 0;
        
        _logger.LogDebug("📡 BROADCASTER: Starting for {ConnectionId}", connectionId);
        
        try
        {
            // Read until channel is closed OR until this cycle is cancelled (VAD detected)
            while (await audioReader.WaitToReadAsync(cancellationToken))
            {
                while (audioReader.TryRead(out var chunk))
                {
                    chunkCount++;
                    totalBytes += chunk.Length;
                    
                    await sttChannel.Writer.WriteAsync(chunk, cancellationToken);
                    await speakerChannel.Writer.WriteAsync(chunk, cancellationToken);
                    
                    // Log every 10 chunks to show progress
                    if (chunkCount % 10 == 0)
                    {
                        _logger.LogDebug("📡 BROADCASTER: Sent {ChunkCount} chunks ({TotalBytes} bytes) to STT/Speaker channels for {ConnectionId}", 
                            chunkCount, totalBytes, connectionId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("🎬 BROADCASTER: Cycle cancelled after {ChunkCount} chunks ({TotalBytes} bytes) for {ConnectionId}", 
                chunkCount, totalBytes, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ BROADCASTER: Error after {ChunkCount} chunks ({TotalBytes} bytes) for {ConnectionId}", 
                chunkCount, totalBytes, connectionId);
        }
        finally
        {
            sttChannel.Writer.TryComplete();
            speakerChannel.Writer.TryComplete();
            _logger.LogDebug("📡 BROADCASTER: Completed - sent {ChunkCount} chunks ({TotalBytes} bytes) for {ConnectionId}", 
                chunkCount, totalBytes, connectionId);
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
            await foreach (var result in _sttService.ProcessAutoLanguageDetectionAsync(audioReader, candidateLanguages, cancellationToken))
            {
                utteranceCollector.AddResult(result);
                
                // Send live transcription updates to frontend
                var displayText = utteranceCollector.GetCurrentDisplayText();
                await _notificationService.NotifyTranscriptionAsync(connectionId, displayText, result.Language, false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(ex, "❌ STT processing error for {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Process speaker identification with "First-Capture-Locking" for zero latency
    /// </summary>
    private async Task ProcessSpeakerIdentificationAsync(
        ChannelReader<byte[]> audioReader,
        MultiLanguageSpeakerAwareUtteranceCollector utteranceCollector,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var audioData = new List<byte[]>();
            var bytesReceived = 0;
            const int MIN_BYTES_FOR_ID = 64000; // ~2 seconds of 16kHz 16-bit audio
            var identificationDone = false;

            await foreach (var chunk in audioReader.ReadAllAsync(cancellationToken))
            {
                if (identificationDone) continue; 

                audioData.Add(chunk);
                bytesReceived += chunk.Length;

                if (bytesReceived >= MIN_BYTES_FOR_ID)
                {
                    var combinedAudio = audioData.SelectMany(x => x).ToArray();
                    var speakerId = await _speakerService.IdentifySpeakerAsync(combinedAudio, connectionId);
                    
                    var fingerprint = new AudioFingerprint
                    {
                        AveragePitch = 150f,
                        MatchConfidence = speakerId != null ? 85f : 30f
                    };

                    utteranceCollector.SetSpeakerContext(speakerId, fingerprint.MatchConfidence, fingerprint);
                    
                    _logger.LogInformation("🎭 SPEAKER LOCK: {SpeakerId} identified via 2s audio for {ConnectionId}",
                        speakerId ?? "unknown", connectionId);

                    identificationDone = true; 
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(ex, "❌ Speaker identification error for {ConnectionId}", connectionId);
        }
    }

    private async Task ProcessCompletedUtteranceAsync(
        string connectionId, 
        MultiLanguageSpeakerAwareUtteranceCollector utteranceCollector,
        ConversationState state)
    {
        try
        {
            var utteranceWithContext = utteranceCollector.GetUtteranceWithResolvedLanguages(
                state.CandidateLanguages,
                state.PrimaryLanguage!);

            var existingSpeakers = _speakerProfileManager.GetSpeakerProfiles(state.SessionId!);
            var genAIResponse = await ProcessWithGenAI(utteranceWithContext, state);
            var speakerInsights = ExtractSpeakerInsights(genAIResponse);

            var speakerDecision = _speakerDecisionEngine.MakeDecision(
                utteranceWithContext, existingSpeakers, speakerInsights);

            var speakerUpdate = _speakerProfileManager.ProcessSpeakerDecision(state.SessionId!, speakerDecision);

            state.LastSpeakerId = speakerDecision.MatchedSpeaker?.SpeakerId ?? speakerDecision.NewSpeaker?.SpeakerId;

            state.StartCompleting();
            await SendConversationResponseAsync(connectionId, utteranceWithContext, genAIResponse, speakerUpdate);
            await _notificationService.NotifyCycleCompletionAsync(connectionId, false);
            
            utteranceCollector.Reset();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error finalising utterance for {ConnectionId}", connectionId);
        }
    }

    private async Task<A3ITranslator.Application.DTOs.Translation.TranslationResponse> ProcessWithGenAI(UtteranceWithContext utterance, ConversationState state)
    {
        var request = new A3ITranslator.Application.DTOs.Translation.EnhancedTranslationRequest
        {
            Text = utterance.Text,
            SourceLanguage = utterance.SourceLanguage,
            TargetLanguage = utterance.TargetLanguage,
            SessionContext = new Dictionary<string, object>
            {
                ["sessionId"] = state.SessionId!,
                ["speakers"] = _speakerProfileManager.GetSpeakerProfiles(state.SessionId!)
                                .Select(s => new { s.SpeakerId, s.DisplayName, s.Insights.AssignedRole }),
                ["lastSpeaker"] = state.LastSpeakerId ?? "None",
                ["audioProvisionalId"] = utterance.ProvisionalSpeakerId ?? "Unknown"
            }
        };

        return await _translationOrchestrator.ProcessTranslationAsync(request);
    }

    private SpeakerInsights? ExtractSpeakerInsights(A3ITranslator.Application.DTOs.Translation.TranslationResponse response)
    {
        try 
        {
            if (response.SpeakerAnalysis != null)
            {
                var sa = response.SpeakerAnalysis;
                var insights = new SpeakerInsights
                {
                    SuggestedName = sa.DetectedName,
                    DetectedGender = Enum.TryParse<SpeakerGender>(sa.SpeakerGender, true, out SpeakerGender g) ? g : SpeakerGender.Unknown,
                    AnalysisConfidence = sa.Confidence * 100f,
                    CommunicationStyle = "Standard", // Default values since LinguisticDNA is not available
                    AssignedRole = null,
                    SentenceComplexity = "Medium",
                    TurnContext = "General",
                    TypicalPhrases = new List<string>()
                };

                return insights;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Could not parse SpeakerInsights from GenAI: {Msg}", ex.Message);
        }
        return new SpeakerInsights { AnalysisConfidence = 0 };
    }

    private async Task SendConversationResponseAsync(string connectionId, UtteranceWithContext utterance, A3ITranslator.Application.DTOs.Translation.TranslationResponse genAIResponse, SpeakerListUpdate speakerUpdate)
    {
        await _notificationService.NotifyTranscriptionAsync(
            connectionId, 
            genAIResponse.ImprovedTranscription ?? utterance.Text, 
            utterance.DominantLanguage, 
            true);

        await _notificationService.NotifyTranslationAsync(
            connectionId, 
            genAIResponse.Translation ?? "Translation error", 
            utterance.TargetLanguage, 
            true);

        if (speakerUpdate.HasChanges)
            await _notificationService.NotifySpeakerUpdateAsync(connectionId, speakerUpdate);

        // TTS stream
        var translatedText = genAIResponse.Translation;
        if (!string.IsNullOrEmpty(translatedText))
            await _ttsService.SynthesizeAndNotifyAsync(connectionId, translatedText, utterance.TargetLanguage);
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
        
        var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
        await _sessionRepository.RemoveByConnectionIdAsync(connectionId, CancellationToken.None);

        if (session != null)
            _speakerProfileManager.ClearSession(session.SessionId);
    }

    private string GenerateAudioHash(byte[] audio) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(audio))[..12];

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
