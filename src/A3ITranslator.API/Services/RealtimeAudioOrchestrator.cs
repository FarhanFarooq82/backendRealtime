using System.Text;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Services.Audio;
using Microsoft.AspNetCore.SignalR;
using A3ITranslator.Application.DTOs.Speaker;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.DTOs.Translation;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.API.Services;

public class RealtimeAudioOrchestrator : IRealtimeAudioOrchestrator
{
    private readonly ISessionManager _sessionManager;
    private readonly IStreamingSTTService _sttService;
    private readonly ISpeakerIdentificationService _speakerIdService;
    private readonly IGenAIService _llmService;
    private readonly IStreamingTTSService _ttsService;
    private readonly IFactExtractionService _factService;
    private readonly IRealtimeNotificationService _notificationService; // ✅ Use consistent interface
    private readonly ILogger<RealtimeAudioOrchestrator> _logger;
    private readonly ISttProcessor _sttProcessor; // 🆕 Add STT processor dependency

    public RealtimeAudioOrchestrator(
        ISessionManager sessionManager,
        IStreamingSTTService sttService,
        ISpeakerIdentificationService speakerIdService,
        IGenAIService llmService,
        IStreamingTTSService ttsService,
        IFactExtractionService factService,
        IRealtimeNotificationService notificationService, // ✅ Framework agnostic
        ISttProcessor sttProcessor, // 🆕 Inject STT processor
        ILogger<RealtimeAudioOrchestrator> logger)
    {
        _sessionManager = sessionManager;
        _sttService = sttService;
        _speakerIdService = speakerIdService;
        _llmService = llmService;
        _ttsService = ttsService;
        _factService = factService;
        _notificationService = notificationService; // ✅ No SignalR dependency
        _sttProcessor = sttProcessor; // 🆕 Assign STT processor
        _logger = logger;
    }

    public async Task ProcessAudioChunkAsync(string connectionId, string base64Chunk)
    {
        try
        {
            if (string.IsNullOrEmpty(base64Chunk))
            {
                _logger.LogWarning("❌ ORCHESTRATOR: Received null or empty audio chunk for {ConnectionId}", connectionId);
                return;
            }

            _logger.LogInformation("🎵🔥 ORCHESTRATOR: ProcessAudioChunkAsync called for {ConnectionId}, chunk size: {Size}", 
                connectionId, base64Chunk.Length);
                
            var session = _sessionManager.GetOrCreateSession(connectionId);
            byte[] rawBytes = Convert.FromBase64String(base64Chunk);
            
            // 🎵 DEBUG: Capture audio chunk for verification
            AudioDebugWriter.Instance.AddAudioChunk(connectionId, rawBytes);
            
            _logger.LogInformation("📡 ORCHESTRATOR: Writing {Size} bytes to AudioStreamChannel for {ConnectionId}", 
                rawBytes.Length, connectionId);
            
            // 🆕 AUTO-START STT: Ensure STT processor is running when audio flows
            Console.WriteLine($"🔍 ORCHESTRATOR DEBUG: Checking STT processor status - SttProcessorRunning: {session.SttProcessorRunning}");
            
            if (!session.SttProcessorRunning)
            {
                session.SttProcessorRunning = true;
                _logger.LogInformation("🚀 ORCHESTRATOR: Auto-starting STT processor for {ConnectionId}", connectionId);
                Console.WriteLine($"🚀 ORCHESTRATOR CONSOLE: Auto-starting STT processor for {connectionId}");
                
                // Start STT processing in background with default language
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine($"🎯 ORCHESTRATOR: Calling _sttProcessor.StartSingleLanguageProcessingAsync for {connectionId}");
                        await _sttProcessor.StartSingleLanguageProcessingAsync(connectionId, "en-US", CancellationToken.None);
                        Console.WriteLine($"✅ ORCHESTRATOR: STT processor completed for {connectionId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ ORCHESTRATOR ERROR: STT processor failed for {connectionId}: {ex.Message}");
                        _logger.LogError(ex, "❌ ORCHESTRATOR: Failed to auto-start STT processor for {ConnectionId}", connectionId);
                        session.SttProcessorRunning = false; // Reset on failure
                    }
                });
            }
            else
            {
                Console.WriteLine($"✅ ORCHESTRATOR DEBUG: STT processor already running for {connectionId}");
            }
            
            // 🔍 DEBUG: Check if channel writer is still available
            Console.WriteLine($"📊 ORCHESTRATOR DEBUG: About to write {rawBytes.Length} bytes to AudioStreamChannel for {connectionId}");
            Console.WriteLine($"📊 ORCHESTRATOR DEBUG: First 10 bytes: [{string.Join(", ", rawBytes.Take(10).Select(b => b.ToString()))}]");
            Console.WriteLine($"📊 ORCHESTRATOR DEBUG: Channel Writer available for writing");
            
            if (session.AudioStreamChannel.Writer.TryWrite(rawBytes))
            {
                _logger.LogInformation("✅ ORCHESTRATOR: Successfully wrote bytes to AudioStreamChannel for {ConnectionId}", connectionId);
                Console.WriteLine($"✅ ORCHESTRATOR CONSOLE: Successfully wrote {rawBytes.Length} bytes to AudioStreamChannel for {connectionId}");
                
                // 🔍 DEBUG: Verify channel has data available to read
                Console.WriteLine($"📊 ORCHESTRATOR DEBUG: Channel write successful - data should be available for STT processor");
            }
            else
            {
                _logger.LogWarning("⚠️ ORCHESTRATOR: Failed to write to AudioStreamChannel - channel may be closed for {ConnectionId}", connectionId);
                Console.WriteLine($"❌ ORCHESTRATOR CONSOLE: Failed TryWrite - trying async WriteAsync for {connectionId}");
                
                // Try async write as fallback
                await session.AudioStreamChannel.Writer.WriteAsync(rawBytes);
                _logger.LogInformation("✅ ORCHESTRATOR: Successfully wrote bytes using async WriteAsync for {ConnectionId}", connectionId);
                Console.WriteLine($"✅ ORCHESTRATOR CONSOLE: Successfully wrote {rawBytes.Length} bytes using async WriteAsync for {connectionId}");
            }
            
            // Also buffer for speaker identification and other processing
            session.AudioBuffer.AddRange(rawBytes);
            
            _logger.LogDebug("📡 Audio chunk processed for {ConnectionId}: {Size} bytes", connectionId, rawBytes.Length);
        }
        catch (InvalidOperationException ioex)
        {
            _logger.LogError(ioex, "❌ ORCHESTRATOR: Channel is closed/completed for {ConnectionId} - cannot write audio chunk", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Audio channel closed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process audio chunk for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Audio processing failed");
        }
    }

    public async Task<string> CommitAndProcessAsync(string connectionId, string language)
    {
        try
        {
            var session = _sessionManager.GetOrCreateSession(connectionId);
            
            // 🎵 DEBUG: Commit audio file when utterance is complete
            AudioDebugWriter.Instance.CommitAudioFile(connectionId, "utterance_complete");
            
            // Get the accumulated transcript from STT processing
            string transcript = session.FinalTranscript?.Trim() ?? "";
            
            _logger.LogInformation("🔍 ORCHESTRATOR: Checking FinalTranscript for {ConnectionId} - Length: {Length}, Content: \"{Content}\"", 
                connectionId, transcript.Length, transcript);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _logger.LogWarning("❌ ORCHESTRATOR: No transcript available for {ConnectionId} - FinalTranscript is empty/null", connectionId);
                // 🎵 DEBUG: Commit audio file even if no transcript for debugging
                AudioDebugWriter.Instance.CommitAudioFile(connectionId, "no_transcript_detected");
                await _notificationService.NotifyTransactionCompleteAsync(connectionId);
                return "No speech detected.";
            }

            _logger.LogInformation("🎯 Processing transcript for {ConnectionId}: {Transcript}", connectionId, transcript);

            // 1. Parallel Speaker Analysis (using buffered audio)
            var speakerTask = AnalyzeSpeakerAsync(session, connectionId, transcript);

            // 2. Generate Response
            string llmResponse = await GenerateResponseAsync(session.SessionId, transcript);

            // 3. Background: Fact Extraction
            _ = ProcessFactsInBackground(speakerTask, session.SessionId, transcript, language);

            // 4. Stream TTS Response
            await StreamTTSResponse(connectionId, llmResponse, language);

            // 5. Signal Completion
            await _notificationService.NotifyTransactionCompleteAsync(connectionId);

            // 6. Cleanup for next utterance
            CleanupSession(session);

            return transcript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process committed audio for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, "Processing failed");
            return "Error occurred during processing.";
        }
    }

    private async Task<SpeakerInfo> AnalyzeSpeakerAsync(dynamic session, string connectionId, string transcript)
    {
        try
        {
            var audioBytes = session.AudioBuffer.ToArray();
            var speakerId = await _speakerIdService.IdentifySpeakerAsync(audioBytes, session.SessionId);
            
            var speakerInfo = new SpeakerInfo { DisplayName = $"Speaker {speakerId}", SpeakerId = speakerId };
            await _notificationService.NotifySpeakerUpdateAsync(connectionId, speakerInfo);
            return speakerInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speaker analysis failed");
            return new SpeakerInfo { DisplayName = "Unknown" };
        }
    }

    private async Task<string> GenerateResponseAsync(string sessionId, string transcript)
    {
        try
        {
            string factContext = await _factService.BuildFactContextAsync(sessionId);
            string systemPrompt = BuildSystemPrompt(factContext);
            return await _llmService.GenerateResponseAsync(systemPrompt, $"Speaker said: {transcript}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM generation failed");
            return "I'm sorry, I couldn't process that.";
        }
    }

    private async Task ProcessFactsInBackground(Task<SpeakerInfo> speakerTask, string sessionId, string transcript, string language)
    {
        try
        {
            var speakerInfo = await speakerTask;
            // Use the correct method signature from IFactExtractionService
            await _factService.BuildFactContextAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background fact processing failed");
        }
    }

    private void CleanupSession(dynamic session)
    {
        try
        {
            // Clear audio buffer but keep the transcript intact for the session
            session.AudioBuffer.Clear();
            
            // Reset final transcript and speaker for next utterance
            session.FinalTranscript = "";
            session.CurrentSpeakerId = null;
            
            _logger.LogDebug("🧹 Session cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Session cleanup failed");
        }
    }

    // 🎵 DEBUG: Cleanup method for disconnection
    public void CleanupAudioDebug(string connectionId)
    {
        try
        {
            AudioDebugWriter.Instance.CleanupSession(connectionId);
            _logger.LogDebug("🧹 Audio debug cleanup completed for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Audio debug cleanup failed for {ConnectionId}", connectionId);
        }
    }

    private string BuildSystemPrompt(string factContext)
    {
        return $@"You are a helpful assistant. Use the following context to answer questions:
{factContext}

Provide clear, concise responses based on the conversation history and facts.";
    }

    private async Task StreamTTSResponse(string connectionId, string response, string language)
    {
        var sentences = SplitIntoSentences(response);
        
        foreach (var sentence in sentences)
        {
            await _notificationService.NotifyTranscriptionAsync(connectionId, sentence, "en-US", true);

            try
            {
                await foreach (var chunk in _ttsService.SynthesizeStreamAsync(sentence, "en-US", "en-US-JennyNeural"))
                {
                    string base64Audio = Convert.ToBase64String(chunk.AudioData);
                    await _notificationService.NotifyAudioChunkAsync(connectionId, base64Audio);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS failed for sentence: {Sentence}", sentence);
            }
        }
    }

    private IEnumerable<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrEmpty(text)) return Enumerable.Empty<string>();
        return text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s));
    }
}
