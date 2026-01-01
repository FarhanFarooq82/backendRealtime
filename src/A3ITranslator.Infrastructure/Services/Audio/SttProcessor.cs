    using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class SttProcessor : ISttProcessor
{
    private readonly IStreamingSTTService _sttService;
    private readonly ISessionManager _sessionManager;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ISpeakerIdentificationService _speakerService;
    private readonly ILogger<SttProcessor> _logger;

    public SttProcessor(
        IStreamingSTTService sttService,
        ISessionManager sessionManager,
        IRealtimeNotificationService notificationService,
        ISpeakerIdentificationService speakerService,
        ILogger<SttProcessor> logger)
    {
        _sttService = sttService;
        _sessionManager = sessionManager;
        _notificationService = notificationService;
        _speakerService = speakerService;
        _logger = logger;
    }

    public async Task StartSingleLanguageProcessingAsync(string connectionId, string language, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🎙️🔥 ENTERING StartSingleLanguageProcessingAsync for {ConnectionId} with language {Language}", connectionId, language);
        Console.WriteLine($"🎙️🔥 CONSOLE: ENTERING StartSingleLanguageProcessingAsync for {connectionId} with language {language}");
        
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null)
        {
            _logger.LogWarning("Failed to get or create session: {ConnectionId}", connectionId);
            Console.WriteLine($"❌ CONSOLE: Failed to get or create session: {connectionId}");
            return;
        }
        
        Console.WriteLine($"✅ CONSOLE: Session found for {connectionId}, proceeding with STT processing");

        var detectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _logger.LogInformation("🎙️ Starting STT for {ConnectionId} in {Language}", connectionId, language);

            // 1. Create channels for STT and Speaker ID
            var sttChannel = Channel.CreateUnbounded<byte[]>();
            var speakerChannel = Channel.CreateUnbounded<byte[]>();

            // 2. Broadcaster (Fan-out)
            var broadcasterTask = Task.Run(async () => 
            {
                var chunkCount = 0;
                try 
                {
                    Console.WriteLine($"📡 STT PROCESSOR: Starting AudioStreamChannel.Reader.ReadAllAsync for {connectionId}");
                    await foreach (var chunk in session.AudioStreamChannel.Reader.ReadAllAsync(detectionCts.Token))
                    {
                        chunkCount++;
                        Console.WriteLine($"📡 STT PROCESSOR: READ CHUNK #{chunkCount} - {chunk.Length} bytes from AudioStreamChannel for {connectionId}");
                        Console.WriteLine($"📡 STT PROCESSOR: First 5 bytes of chunk #{chunkCount}: [{string.Join(", ", chunk.Take(5).Select(b => b.ToString()))}]");
                        
                        await sttChannel.Writer.WriteAsync(chunk, detectionCts.Token);
                        await speakerChannel.Writer.WriteAsync(chunk, detectionCts.Token);
                        
                        Console.WriteLine($"📡 STT PROCESSOR: Successfully wrote chunk #{chunkCount} to both STT and Speaker channels");
                    }
                    Console.WriteLine($"📡 STT PROCESSOR: AudioStreamChannel reading completed, processed {chunkCount} chunks for {connectionId}");
                }
                catch (OperationCanceledException) 
                { 
                    Console.WriteLine($"📡 STT PROCESSOR: AudioStreamChannel reading cancelled after {chunkCount} chunks for {connectionId}");
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine($"❌ STT PROCESSOR: AudioStreamChannel reading error after {chunkCount} chunks for {connectionId}: {ex.Message}");
                    _logger.LogError(ex, "Single-language broadcaster error"); 
                }
                finally 
                {
                    Console.WriteLine($"📡 STT PROCESSOR: Completing STT and Speaker channels for {connectionId}");
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
                    _logger.LogInformation("🎤 STT PROCESSOR: Starting TranscribeStreamAsync for {ConnectionId} with {Language}", connectionId, language);
                    Console.WriteLine($"🎤 CONSOLE: STT PROCESSOR Starting TranscribeStreamAsync for {connectionId} with {language}");
                    var resultCount = 0;
                    
                    Console.WriteLine($"📡 CONSOLE: About to call _sttService.TranscribeStreamAsync...");
                    await foreach (var result in _sttService.TranscribeStreamAsync(sttChannel.Reader, language, detectionCts.Token))
                    {
                        resultCount++;
                        _logger.LogInformation("🎤 STT PROCESSOR: Received result #{Count} from STT service for {ConnectionId}", resultCount, connectionId);
                        Console.WriteLine($"🎤 CONSOLE: STT PROCESSOR Received result #{resultCount}: {result.Text}");
                        await ProcessTranscriptionResult(connectionId, result, language);
                    }
                    
                    _logger.LogInformation("🎬 STT PROCESSOR: TranscribeStreamAsync completed for {ConnectionId}, processed {Count} results", connectionId, resultCount);
                    Console.WriteLine($"🎬 CONSOLE: STT PROCESSOR TranscribeStreamAsync completed for {connectionId}, processed {resultCount} results");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🛑 STT PROCESSOR: TranscribeStreamAsync cancelled for {ConnectionId}", connectionId);
                    Console.WriteLine($"🛑 CONSOLE: STT PROCESSOR TranscribeStreamAsync cancelled for {connectionId}");
                }
                catch (Exception ex) 
                { 
                    _logger.LogError(ex, "❌ STT PROCESSOR: Single-language STT service error for {ConnectionId}", connectionId);
                    Console.WriteLine($"❌ CONSOLE: STT PROCESSOR Exception: {ex.Message}");
                    Console.WriteLine($"❌ CONSOLE: STT PROCESSOR Stack Trace: {ex.StackTrace}");
                }
            }, detectionCts.Token);

            await Task.WhenAny(sttTask, broadcasterTask, speakerTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT error for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Transcription failed");
        }
        finally
        {
            detectionCts.Cancel();
            detectionCts.Dispose();
        }
    }

    public async Task StartMultiLanguageDetectionAsync(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔍🔥 ENTERING StartMultiLanguageDetectionAsync for {ConnectionId} with languages: [{Languages}]", 
            connectionId, string.Join(", ", candidateLanguages));
            
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null) 
        {
            _logger.LogWarning("Failed to get or create session: {ConnectionId}", connectionId);
            return;
        }

        try
        {
            // 🎯 Check if language is already confirmed from session or speaker history
            var confirmedLanguage = session.GetEffectiveLanguage();
            if (session.IsLanguageConfirmed)
            {
                _logger.LogInformation("🎯 Language already confirmed as {Language}, switching to single language processing", confirmedLanguage);
                await _notificationService.NotifyLanguageDetectedAsync(connectionId, confirmedLanguage);
                await StartSingleLanguageProcessingAsync(connectionId, confirmedLanguage, cancellationToken);
                return;
            }

            // 🔥 Start detection immediately without blocking for speaker ID
            await StartConcurrentLanguageProcessingAsync(connectionId, candidateLanguages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Language detection error for {ConnectionId}", connectionId);
            var fallback = candidateLanguages.FirstOrDefault() ?? "en";
            await StartSingleLanguageProcessingAsync(connectionId, fallback, cancellationToken);
        }
    }

    private async Task StartConcurrentLanguageProcessingAsync(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀🔥 ENTERING StartConcurrentLanguageProcessingAsync for {ConnectionId} with {Count} languages", 
            connectionId, candidateLanguages.Length);
            
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null) 
        {
            _logger.LogWarning("Failed to get or create session for concurrent processing: {ConnectionId}", connectionId);
            return;
        }

        var languageResults = new Dictionary<string, List<TranscriptionResult>>();
        var languageConfidences = new Dictionary<string, double>();
        var detectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Sanitize and filter duplicate/empty languages
        var uniqueLanguages = candidateLanguages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();

        if (!uniqueLanguages.Any())
        {
            _logger.LogWarning("No valid candidate languages provided for {ConnectionId}. Falling back to defaults.", connectionId);
            uniqueLanguages.Add("en-US");
            uniqueLanguages.Add("es-ES");
        }

        try
        {
            // 1. Create channels for EACH language + Speaker ID
            var channels = uniqueLanguages.ToDictionary(
                lang => lang, 
                _ => Channel.CreateUnbounded<byte[]>()
            );
            var speakerChannel = Channel.CreateUnbounded<byte[]>();

            // 2. Specialized Broadcaster
            var broadcasterTask = Task.Run(async () => 
            {
                try 
                {
                    _logger.LogInformation("📡 Smart Broadcaster started for {ConnectionId}", connectionId);
                    await foreach (var chunk in session.AudioStreamChannel.Reader.ReadAllAsync(detectionCts.Token))
                    {
                        foreach (var channel in channels.Values) await channel.Writer.WriteAsync(chunk);
                        await speakerChannel.Writer.WriteAsync(chunk);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogError(ex, "Broadcaster error"); }
                finally 
                {
                    foreach (var channel in channels.Values) channel.Writer.TryComplete();
                    speakerChannel.Writer.TryComplete();
                }
            }, detectionCts.Token);

            // 3. Parallel Background Tasks
            var speakerTask = IdentifySpeakerAsync(session, connectionId, speakerChannel.Reader);
            var monitoringTask = MonitorLanguageConfidence(connectionId, uniqueLanguages.ToArray(), languageResults, languageConfidences, detectionCts);
            
            var sttTasks = uniqueLanguages.Select(language => 
                ProcessLanguageConcurrently(connectionId, language, channels[language].Reader, languageResults, languageConfidences, detectionCts.Token)
            ).ToArray();

            // Wait until a winner is found or connection drops
            await Task.WhenAny(sttTasks.Concat(new[] { monitoringTask, broadcasterTask, speakerTask }).ToArray());
        }
        finally
        {
            detectionCts.Cancel(); // Stop all concurrent processing
            detectionCts.Dispose();
        }
    }

    private async Task ProcessLanguageConcurrently(
        string connectionId, 
        string language, 
        ChannelReader<byte[]> languageReader,
        Dictionary<string, List<TranscriptionResult>> languageResults,
        Dictionary<string, double> languageConfidences,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("🎤🔥 ENTERING ProcessLanguageConcurrently for {ConnectionId} - {Language}", connectionId, language);
        
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null) 
        {
            _logger.LogWarning("Failed to get or create session for language processing: {ConnectionId}", connectionId);
            return;
        }

        try
        {
            if (!languageResults.ContainsKey(language))
            {
                languageResults[language] = new List<TranscriptionResult>();
                languageConfidences[language] = 0.0;
            }

            await foreach (var result in _sttService.TranscribeStreamAsync(languageReader, language, cancellationToken))
            {
                languageResults[language].Add(result);
                
                // Update running confidence - prioritize Final results
                var results = languageResults[language];
                if (results.Any())
                {
                    languageConfidences[language] = results.Average(r => r.IsFinal ? r.Confidence : r.Confidence * 0.8);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogDebug("Concurrent STT stopped for {Language}: {Message}", language, ex.Message); }
    }

    private async Task MonitorLanguageConfidence(
        string connectionId,
        string[] candidateLanguages,
        Dictionary<string, List<TranscriptionResult>> languageResults,
        Dictionary<string, double> languageConfidences,
        CancellationTokenSource detectionCts)
    {
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null) 
        {
            _logger.LogWarning("Failed to get or create session for language monitoring: {ConnectionId}", connectionId);
            return;
        }

        const double CONFIDENCE_THRESHOLD = 0.75;
        const int MIN_RESULTS = 2;

        try
        {
            while (!detectionCts.Token.IsCancellationRequested)
            {
                await Task.Delay(400, detectionCts.Token);

                // 🚀 OPTION 1: LOCK-IN BY SPEAKER
                if (!string.IsNullOrEmpty(session.CurrentSpeakerId))
                {
                    var speaker = session.Speakers.GetSpeaker(session.CurrentSpeakerId);
                    if (speaker != null && !string.IsNullOrEmpty(speaker.KnownLanguage))
                    {
                        if (candidateLanguages.Contains(speaker.KnownLanguage))
                        {
                            _logger.LogInformation("🎯 SMART LOCK: Speaker identified as {Id} with language {Lang}. Stopping detection.", 
                                session.CurrentSpeakerId, speaker.KnownLanguage);
                            await ConfirmLanguageAndSwitch(connectionId, speaker.KnownLanguage, languageResults, session, detectionCts);
                            return;
                        }
                    }
                }

                // 🎯 OPTION 2: LOCK-IN BY CONFIDENCE GAP
                var qualified = languageConfidences
                    .Where(kvp => languageResults[kvp.Key].Count >= MIN_RESULTS)
                    .OrderByDescending(kvp => kvp.Value)
                    .ToList();

                if (qualified.Count >= 1)
                {
                    var best = qualified[0];
                    var secondBestVal = qualified.Count > 1 ? qualified[1].Value : 0.0;
                    var gap = best.Value - secondBestVal;

                    // If we have a high confidence clear winner
                    if (best.Value > CONFIDENCE_THRESHOLD && (gap > 0.2 || languageResults[best.Key].Count > 6))
                    {
                        _logger.LogInformation("🎯 SMART LOCK: Language {Lang} won by confidence (val: {V:F2}, gap: {G:F2})", 
                            best.Key, best.Value, gap);
                        await ConfirmLanguageAndSwitch(connectionId, best.Key, languageResults, session, detectionCts);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ConfirmLanguageAndSwitch(
        string connectionId, 
        string confirmedLanguage, 
        Dictionary<string, List<TranscriptionResult>> languageResults,
        ConversationSession session,
        CancellationTokenSource detectionCts)
    {
        // 1. Notify frontend
        await _notificationService.NotifyLanguageDetectedAsync(connectionId, confirmedLanguage);

        // 2. Playback back-buffered results for the winner
        if (languageResults.TryGetValue(confirmedLanguage, out var results))
        {
            foreach (var r in results) 
                await ProcessTranscriptionResult(connectionId, r, confirmedLanguage);
        }

        // 3. Serial switch
        detectionCts.Cancel();
        _ = Task.Run(async () =>
        {
            await StartSingleLanguageProcessingAsync(connectionId, confirmedLanguage, CancellationToken.None);
        });
    }

    private async Task IdentifySpeakerAsync(ConversationSession session, string connectionId, ChannelReader<byte[]> reader)
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

    private string GetSpeakerDisplayName(ConversationSession session, string? speakerId)
    {
        if (string.IsNullOrEmpty(speakerId)) return "Unknown Speaker";
        
        var speaker = session.Speakers.GetSpeaker(speakerId);
        return speaker?.DisplayName ?? $"Speaker {speakerId[..Math.Min(8, speakerId.Length)]}";
    }

    private async Task ProcessTranscriptionResult(string connectionId, TranscriptionResult result, string language)
    {
        var session = _sessionManager.GetOrCreateSession(connectionId);
        if (session == null) 
        {
            _logger.LogWarning("⚠️ STT PROCESSOR: Session not found for {ConnectionId}", connectionId);
            return;
        }

        _logger.LogInformation("🎤 STT PROCESSOR: Received transcription result for {ConnectionId} - Text: \"{Text}\", IsFinal: {IsFinal}, Confidence: {Confidence}", 
            connectionId, result.Text, result.IsFinal, result.Confidence);

        if (result.IsFinal)
        {
            _logger.LogInformation("✅ STT PROCESSOR: Adding FINAL result to session.FinalTranscript for {ConnectionId}: \"{Text}\"", connectionId, result.Text);
            session.FinalTranscript += result.Text + " ";
            
            _logger.LogInformation("📝 STT PROCESSOR: Current FinalTranscript for {ConnectionId}: \"{Transcript}\"", connectionId, session.FinalTranscript);
            
            var turn = new ConversationTurn
            {
                SpeakerId = session.CurrentSpeakerId ?? "unknown",
                SpeakerName = GetSpeakerDisplayName(session, session.CurrentSpeakerId),
                Language = language,
                OriginalText = result.Text,
                Confidence = (float)result.Confidence,
                Type = TurnType.Speech
            };
            
            await _sessionManager.AddConversationTurnAsync(connectionId, turn);
        }
        else
        {
            _logger.LogDebug("🔄 STT PROCESSOR: Received interim result for {ConnectionId}: \"{Text}\"", connectionId, result.Text);
        }

        await _notificationService.NotifyTranscriptionAsync(connectionId, result.Text, result.Language, result.IsFinal);
    }
}