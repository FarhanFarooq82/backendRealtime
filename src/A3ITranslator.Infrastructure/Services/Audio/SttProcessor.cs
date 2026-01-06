using A3ITranslator.Application.Services;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.DTOs.Translation; // 🆕 Add DTOs for translation
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

// ✅ PURE DOMAIN: Type aliases for clean architecture
using DomainSession = A3ITranslator.Application.Domain.Entities.ConversationSession;

namespace A3ITranslator.Infrastructure.Services.Audio;

// 🔄 VAD-based utterance accumulator for multiple utterances
public class UtteranceCollector
{
    private readonly List<string> _finalUtterances = new(); // Accumulate final utterances
    private DateTime _lastResultTime = DateTime.UtcNow;
    private readonly TimeSpan _vadTimeout = TimeSpan.FromSeconds(3); // 3-second VAD timeout
    private string _currentInterimText = string.Empty; // Track latest interim for display

    public void AddResult(TranscriptionResult result)
    {
        _lastResultTime = DateTime.UtcNow;

        if (result.IsFinal)
        {
            // Add final utterance to accumulation
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                _finalUtterances.Add(result.Text.Trim());
            }
            _currentInterimText = string.Empty; // Clear interim when final arrives
        }
        else
        {
            // Update current interim text for display
            _currentInterimText = result.Text;
        }
    }

    public string GetAccumulatedText()
    {
        // Return all accumulated final utterances as single text
        return string.Join(" ", _finalUtterances).Trim();
    }

    public string GetCurrentDisplayText()
    {
        // Return accumulated final text + current interim for live display
        var accumulated = GetAccumulatedText();
        if (!string.IsNullOrWhiteSpace(_currentInterimText))
        {
            return string.IsNullOrWhiteSpace(accumulated) 
                ? _currentInterimText 
                : $"{accumulated} {_currentInterimText}";
        }
        return accumulated;
    }

    public bool HasTimedOut() => DateTime.UtcNow - _lastResultTime >= _vadTimeout;

    public void Reset()
    {
        _finalUtterances.Clear();
        _currentInterimText = string.Empty;
        _lastResultTime = DateTime.UtcNow;
    }

    public bool HasAccumulatedText => _finalUtterances.Any();
    public int UtteranceCount => _finalUtterances.Count;
}

public class SttProcessor : ISttProcessor, IDisposable
{
    private readonly IStreamingSTTService _sttService;
    private readonly ISessionRepository _sessionManager;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ISpeakerIdentificationService _speakerService;
    private readonly ITranslationOrchestrator _translationOrchestrator; // 🆕 Add translation orchestrator for GenAI flow
    private readonly ILogger<SttProcessor> _logger;
    
    // 🔄 VAD-based utterance collection per connection
    private readonly Dictionary<string, UtteranceCollector> _utteranceCollectors = new();
    
    // 🕒 Background VAD timeout checker
    private readonly Timer _vadTimeoutChecker;

    public SttProcessor(
        IStreamingSTTService sttService,
        ISessionRepository sessionRepository, // ✅ PURE DOMAIN: Use Domain repository
        IRealtimeNotificationService notificationService,
        ISpeakerIdentificationService speakerService,
        ITranslationOrchestrator translationOrchestrator, // 🆕 Add translation orchestrator for GenAI flow
        ILogger<SttProcessor> logger)
    {
        _sttService = sttService;
        _sessionManager = sessionRepository; // ✅ PURE DOMAIN: Store repository in field
        _notificationService = notificationService;
        _speakerService = speakerService;
        _translationOrchestrator = translationOrchestrator;
        _logger = logger;
        
        // 🕒 Start background VAD timeout checker (every 1 second)
        _vadTimeoutChecker = new Timer(CheckVadTimeouts, null, 1000, 1000);
    }

    public async Task StartAutoLanguageDetectionAsync(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🌍🔥 ENTERING StartAutoLanguageDetectionAsync for {ConnectionId} with languages: [{Languages}]", 
            connectionId, string.Join(", ", candidateLanguages));
        Console.WriteLine($"🌍🔥 CONSOLE: ENTERING StartAutoLanguageDetectionAsync for {connectionId} with languages: [{string.Join(", ", candidateLanguages)}]");

        // ✅ PURE DOMAIN: Use Domain repository to get session
        var session = await _sessionManager.GetByConnectionIdAsync(connectionId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Failed to get or create session: {ConnectionId}", connectionId);
            Console.WriteLine($"❌ CONSOLE: Failed to get or create session: {connectionId}");
            return;
        }

        Console.WriteLine($"✅ CONSOLE: Session found for {connectionId}, proceeding with auto-detection STT processing");

        var detectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _logger.LogInformation("🌍 Starting Google Auto-Detection STT for {ConnectionId}", connectionId);

            // 1. Create channels for STT and Speaker ID
            var sttChannel = Channel.CreateUnbounded<byte[]>();
            var speakerChannel = Channel.CreateUnbounded<byte[]>();

            // 2. Broadcaster (Fan-out)
            var broadcasterTask = Task.Run(async () => 
            {
                var chunkCount = 0;
                try 
                {
                    Console.WriteLine($"📡 AUTO-DETECTION: Starting AudioStreamChannel.Reader.ReadAllAsync for {connectionId}");
                    await foreach (var chunk in session.AudioStreamChannel.Reader.ReadAllAsync(detectionCts.Token))
                    {
                        chunkCount++;
                        Console.WriteLine($"📡 AUTO-DETECTION: READ CHUNK #{chunkCount} - {chunk.Length} bytes from AudioStreamChannel for {connectionId}");
                        
                        await sttChannel.Writer.WriteAsync(chunk, detectionCts.Token);
                        await speakerChannel.Writer.WriteAsync(chunk, detectionCts.Token);
                        
                        Console.WriteLine($"📡 AUTO-DETECTION: Successfully wrote chunk #{chunkCount} to both STT and Speaker channels");
                    }
                    Console.WriteLine($"📡 AUTO-DETECTION: AudioStreamChannel reading completed, processed {chunkCount} chunks for {connectionId}");
                }
                catch (OperationCanceledException) 
                { 
                    Console.WriteLine($"📡 AUTO-DETECTION: AudioStreamChannel reading cancelled after {chunkCount} chunks for {connectionId}");
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"❌ AUTO-DETECTION: AudioStreamChannel reading error after {chunkCount} chunks for {connectionId}: {ex.Message}");
                    _logger.LogError(ex, "Auto-detection broadcaster error"); 
                }
                finally 
                {
                    Console.WriteLine($"📡 AUTO-DETECTION: Completing STT and Speaker channels for {connectionId}");
                    sttChannel.Writer.TryComplete();
                    speakerChannel.Writer.TryComplete();
                }
            }, detectionCts.Token);

            // 3. Parallel Tasks
            var speakerTask = IdentifySpeakerAsync(session, connectionId, speakerChannel.Reader);
            
            var sttTask = Task.Run(async () => 
            {
                try
                {
                    _logger.LogInformation("🌍 AUTO-DETECTION: Starting ProcessAutoLanguageDetectionAsync for {ConnectionId}", connectionId);
                    Console.WriteLine($"🌍 CONSOLE: AUTO-DETECTION Starting ProcessAutoLanguageDetectionAsync for {connectionId}");
                    var resultCount = 0;
                    string? detectedLanguage = null;
                    
                    Console.WriteLine($"📡 CONSOLE: About to call _sttService.ProcessAutoLanguageDetectionAsync...");
                    await foreach (var result in _sttService.ProcessAutoLanguageDetectionAsync(sttChannel.Reader, candidateLanguages, detectionCts.Token))
                    {
                        resultCount++;
                        _logger.LogInformation("🌍 AUTO-DETECTION: Received result #{Count} from STT service for {ConnectionId} - Language: {Language}", 
                            resultCount, connectionId, result.Language);
                        Console.WriteLine($"🌍 CONSOLE: AUTO-DETECTION Received result #{resultCount}: \"{result.Text}\" (Language: {result.Language})");
                        
                        // Track detected language and update session
                        if (detectedLanguage == null || detectedLanguage != result.Language)
                        {
                            detectedLanguage = result.Language;
                            session.PrimaryLanguage = detectedLanguage;
                            session.IsLanguageConfirmed = true;
                            await _sessionManager.SaveAsync(session, CancellationToken.None);
                            
                            _logger.LogInformation("🎯 AUTO-DETECTION: Language confirmed as {Language} for {ConnectionId}", detectedLanguage, connectionId);
                            Console.WriteLine($"🎯 CONSOLE: AUTO-DETECTION Language confirmed as {detectedLanguage} for {connectionId}");
                            
                            await _notificationService.NotifyLanguageDetectedAsync(connectionId, detectedLanguage);
                        }
                        
                        await ProcessTranscriptionResult(connectionId, result, detectedLanguage);
                    }
                    
                    _logger.LogInformation("🎬 AUTO-DETECTION: ProcessAutoLanguageDetectionAsync completed for {ConnectionId}, processed {Count} results", connectionId, resultCount);
                    Console.WriteLine($"🎬 CONSOLE: AUTO-DETECTION ProcessAutoLanguageDetectionAsync completed for {connectionId}, processed {resultCount} results");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🛑 AUTO-DETECTION: ProcessAutoLanguageDetectionAsync cancelled for {ConnectionId}", connectionId);
                    Console.WriteLine($"🛑 CONSOLE: AUTO-DETECTION ProcessAutoLanguageDetectionAsync cancelled for {connectionId}");
                }
                catch (Exception ex) 
                { 
                    _logger.LogError(ex, "❌ AUTO-DETECTION: STT service error for {ConnectionId}", connectionId);
                    Console.WriteLine($"❌ CONSOLE: AUTO-DETECTION Exception: {ex.Message}");
                }
            }, detectionCts.Token);

            await Task.WhenAny(sttTask, broadcasterTask, speakerTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Auto-detection STT error for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Auto-detection transcription failed");
        }
        finally
        {
            detectionCts.Cancel();
            detectionCts.Dispose();
        }
    }

    private async Task IdentifySpeakerAsync(DomainSession session, string connectionId, ChannelReader<byte[]> reader) // ✅ PURE DOMAIN: Use Domain session
    {
        _logger.LogInformation("👤 Parallel Speaker Analysis started for {ConnectionId}", connectionId);
        var accumulationBuffer = new List<byte>();
        const int MIN_BYTES_FOR_ANALYSIS = 32000; // ~1 second of audio (16khz, mono)

        try
        {
            await foreach (var chunk in reader.ReadAllAsync())
            {
                if (!string.IsNullOrEmpty(session.CurrentSpeakerId)) continue; 

                accumulationBuffer.AddRange(chunk);

                if (accumulationBuffer.Count >= MIN_BYTES_FOR_ANALYSIS)
                {
                    var data = accumulationBuffer.ToArray();
                    accumulationBuffer.Clear(); // Flush for next attempt if needed

                    _logger.LogDebug("👤 Analyzing speaker profile (buffer size: {S} bytes)", data.Length);
                    var speakerId = await _speakerService.IdentifyOrCreateSpeakerAsync(data, connectionId);
                    
                    if (!string.IsNullOrEmpty(speakerId))
                    {
                        session.CurrentSpeakerId = speakerId;
                        _logger.LogInformation("🎯 Speaker Locked: {SpeakerId}", speakerId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Speaker Analysis Error"); }
    }

    private string GetSpeakerDisplayName(DomainSession session, string? speakerId) // ✅ PURE DOMAIN: Use Domain session
    {
        if (string.IsNullOrEmpty(speakerId)) return "Unknown Speaker";
        
        // ✅ PURE DOMAIN: Use Domain session method
        var speaker = session.GetSpeaker(speakerId);
        return speaker?.DisplayName ?? $"Speaker {speakerId[..Math.Min(8, speakerId.Length)]}";
    }

    private async Task ProcessTranscriptionResult(string connectionId, TranscriptionResult result, string language)
    {
        // ✅ PURE DOMAIN: Use Domain repository to get session
        var session = await _sessionManager.GetByConnectionIdAsync(connectionId, CancellationToken.None);
        if (session == null) 
        {
            _logger.LogWarning("⚠️ STT PROCESSOR: Session not found for {ConnectionId}", connectionId);
            return;
        }

        _logger.LogInformation("🎤 STT PROCESSOR: Received transcription result for {ConnectionId} - Text: \"{Text}\", IsFinal: {IsFinal}, Confidence: {Confidence}", 
            connectionId, result.Text, result.IsFinal, result.Confidence);

        // 🛡️ FILTER: Skip error results to prevent corrupting good transcripts
        if (result.Text.Contains("[Google WebM Error Fallback]") || 
            result.Text.Contains("Processing failed") ||
            result.Text.Contains("Error") && result.Confidence < 0.5)
        {
            _logger.LogWarning("🛡️ STT PROCESSOR: Filtering out error result for {ConnectionId}: \"{Text}\" (Confidence: {Confidence})", 
                connectionId, result.Text, result.Confidence);
            return;
        }

        // 🔄 VAD-based utterance accumulation - collect all utterances until VAD timeout
        if (!_utteranceCollectors.ContainsKey(connectionId))
        {
            _utteranceCollectors[connectionId] = new UtteranceCollector();
        }

        var collector = _utteranceCollectors[connectionId];
        collector.AddResult(result);

        _logger.LogInformation("📥 STT PROCESSOR: Added result to utterance collector for {ConnectionId}: \"{Text}\" (IsFinal: {IsFinal}, Accumulated: {Count} utterances)", 
            connectionId, result.Text, result.IsFinal, collector.UtteranceCount);

        // Only trigger translation on VAD timeout (NOT on final results)
        if (collector.HasTimedOut() && collector.HasAccumulatedText)
        {
            var accumulatedText = collector.GetAccumulatedText();
            _logger.LogInformation("🎯 STT PROCESSOR: VAD TIMEOUT detected for {ConnectionId} - sending accumulated text to translation: \"{Text}\"", 
                connectionId, accumulatedText);

            // Add accumulated text to session transcript and conversation turns
            session.AppendTranscript(accumulatedText + " ");
            
            var speakerName = GetSpeakerDisplayName(session, session.CurrentSpeakerId);
            var turn = A3ITranslator.Application.Domain.Entities.ConversationTurn.CreateSpeech(
                session.CurrentSpeakerId ?? "unknown",
                speakerName,
                accumulatedText,
                language
            );
            
            session.AddConversationTurn(turn);
            await _sessionManager.SaveAsync(session, CancellationToken.None);

            // 🚀 VAD-TIMEOUT TRANSLATION: Send accumulated text to GenAI
            _logger.LogInformation("🚀 STT PROCESSOR: VAD-timeout translation starting for {ConnectionId}", connectionId);
            
            // Send audio reception acknowledgment to frontend immediately
            await _notificationService.NotifyAudioReceptionAckAsync(connectionId, "Speech completed - generating response...");
            
            // Start background GenAI processing
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("🤖 Starting VAD-timeout GenAI processing for {ConnectionId} with accumulated text", connectionId);
                    Console.WriteLine($"🤖 VAD-TIMEOUT GenAI: Starting processing for {connectionId} with: \"{accumulatedText}\"");
                    
                    var translationRequest = new EnhancedTranslationRequest
                    {
                        Text = accumulatedText,
                        SourceLanguage = language,
                        TargetLanguage = "en-US", // TODO: Make configurable
                        SessionId = connectionId
                    };
                    
                    var translationResponse = await _translationOrchestrator.ProcessTranslationAsync(translationRequest);
                    
                    Console.WriteLine($"✅ VAD-timeout translation completed: \"{translationResponse.Translation}\"");
                    Console.WriteLine($"🎯 Intent: {translationResponse.Intent}, Confidence: {translationResponse.Confidence:F2}");
                    
                    // Send translation result notification
                    await _notificationService.NotifyTranscriptionAsync(
                        connectionId, 
                        translationResponse.Translation, 
                        "en-US", 
                        true
                    );
                    
                    Console.WriteLine($"✅ VAD-TIMEOUT GenAI: Completed processing for {connectionId}");
                    _logger.LogInformation("✅ VAD-timeout GenAI processing completed for {ConnectionId}", connectionId);
                }
                catch (Exception genAiEx)
                {
                    _logger.LogError(genAiEx, "❌ VAD-timeout GenAI processing failed for {ConnectionId}", connectionId);
                    Console.WriteLine($"❌ VAD-TIMEOUT GenAI: Error for {connectionId}: {genAiEx.Message}");
                    
                    try
                    {
                        await _notificationService.NotifyErrorAsync(connectionId, "Response generation failed - please try again");
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogError(notifyEx, "❌ Failed to send error notification for {ConnectionId}", connectionId);
                    }
                }
            });

            // Reset collector for next speech segment
            collector.Reset();
            _logger.LogInformation("🔄 STT PROCESSOR: Utterance collector reset for {ConnectionId} - ready for next speech segment", connectionId);
        }
        else
        {
            if (result.IsFinal)
            {
                _logger.LogInformation("✅ STT PROCESSOR: Final utterance collected for {ConnectionId}: \"{Text}\" - waiting for VAD timeout", 
                    connectionId, result.Text);
            }
            else
            {
                _logger.LogDebug("⏳ STT PROCESSOR: Interim result for {ConnectionId}: \"{Text}\" - continuing to collect", 
                    connectionId, result.Text);
            }
        }

        // Always send current display text to frontend for live transcription
        var currentDisplayText = collector.GetCurrentDisplayText();
        await _notificationService.NotifyTranscriptionAsync(connectionId, currentDisplayText, result.Language, false);
    }

    // 🧹 Cleanup method to prevent memory leaks
    public void CleanupConnection(string connectionId)
    {
        if (_utteranceCollectors.ContainsKey(connectionId))
        {
            _utteranceCollectors.Remove(connectionId);
            _logger.LogInformation("🧹 STT PROCESSOR: Cleaned up utterance collector for {ConnectionId}", connectionId);
        }
    }

    // 🕒 Background VAD timeout checker
    private async void CheckVadTimeouts(object? state)
    {
        var connectionsToProcess = new List<string>();
        
        // Find connections that have timed out
        foreach (var kvp in _utteranceCollectors.ToList())
        {
            if (kvp.Value.HasTimedOut() && kvp.Value.HasAccumulatedText)
            {
                connectionsToProcess.Add(kvp.Key);
            }
        }

        // Process each timed-out connection
        foreach (var connectionId in connectionsToProcess)
        {
            try
            {
                var collector = _utteranceCollectors[connectionId];
                var accumulatedText = collector.GetAccumulatedText();
                
                _logger.LogInformation("🕒 BACKGROUND VAD TIMEOUT: Processing accumulated text for {ConnectionId}: \"{Text}\"", 
                    connectionId, accumulatedText);

                // Get session for this connection
                var session = await _sessionManager.GetByConnectionIdAsync(connectionId, CancellationToken.None);
                if (session == null) continue;

                // Add accumulated text to session
                session.AppendTranscript(accumulatedText + " ");
                
                var speakerName = GetSpeakerDisplayName(session, session.CurrentSpeakerId);
                var turn = A3ITranslator.Application.Domain.Entities.ConversationTurn.CreateSpeech(
                    session.CurrentSpeakerId ?? "unknown",
                    speakerName,
                    accumulatedText,
                    session.GetEffectiveLanguage() // Use domain method to get language
                );
                
                session.AddConversationTurn(turn);
                await _sessionManager.SaveAsync(session, CancellationToken.None);

                // Send acknowledgment
                await _notificationService.NotifyAudioReceptionAckAsync(connectionId, "Speech completed - generating response...");
                
                // Trigger translation
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var translationRequest = new EnhancedTranslationRequest
                        {
                            Text = accumulatedText,
                            SourceLanguage = session.GetEffectiveLanguage(),
                            TargetLanguage = "en-US",
                            SessionId = connectionId
                        };
                        
                        var translationResponse = await _translationOrchestrator.ProcessTranslationAsync(translationRequest);
                        
                        await _notificationService.NotifyTranscriptionAsync(
                            connectionId, 
                            translationResponse.Translation, 
                            "en-US", 
                            true
                        );
                        
                        _logger.LogInformation("✅ BACKGROUND VAD TIMEOUT: GenAI processing completed for {ConnectionId}", connectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ BACKGROUND VAD TIMEOUT: GenAI processing failed for {ConnectionId}", connectionId);
                    }
                });

                // Reset collector
                collector.Reset();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in background VAD timeout processing for {ConnectionId}", connectionId);
            }
        }
    }

    // 🧹 IDisposable implementation
    public void Dispose()
    {
        _vadTimeoutChecker?.Dispose();
        GC.SuppressFinalize(this);
    }
}