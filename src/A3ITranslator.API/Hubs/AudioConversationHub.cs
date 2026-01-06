using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Interfaces;
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Helpers;
using System.Threading.Channels;
using MediatR;
using A3ITranslator.Application.Features.Conversation.Commands.StartSession;
using A3ITranslator.Application.Features.Conversation.Commands.CommitUtterance;
using A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;
using A3ITranslator.Application.Domain.Interfaces;

namespace A3ITranslator.API.Hubs;

/// <summary>
/// Clean SignalR Hub - Pure Domain Architecture
/// </summary>
public class AudioConversationHub : Hub<IAudioClient>, IDisposable
{
    private readonly ILogger<AudioConversationHub> _logger;
    private readonly ISessionRepository _sessionRepository; // ✅ Only Domain interface
    private readonly ILanguageDetectionService _languageService;
    private readonly ISttProcessor _sttProcessor;
    private readonly IMediator _mediator;
    private readonly AudioTestCollector _audioTestCollector;
    private readonly CancellationTokenSource _hubCancellationTokenSource = new();
    private bool _disposed = false;

    // ✅ Clean constructor - Pure Domain Architecture
    public AudioConversationHub(
        ILogger<AudioConversationHub> logger,
        ISessionRepository sessionRepository, // ✅ Only Domain interface
        ILanguageDetectionService languageService,
        ISttProcessor sttProcessor,
        IMediator mediator,
        AudioTestCollector audioTestCollector)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
        _languageService = languageService;
        _sttProcessor = sttProcessor;
        _mediator = mediator;
        _audioTestCollector = audioTestCollector;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"🔌 CONSOLE: OnConnectedAsync ENTRY for {connectionId}");
        _logger.LogInformation("🔌 OnConnectedAsync ENTRY for {ConnectionId}", connectionId);
        
        try
        {
            _logger.LogInformation("🔌 New SignalR connection: {ConnectionId}", connectionId);
            Console.WriteLine($"🔌 CONSOLE: New SignalR connection: {connectionId}");
            
            var httpContext = Context.GetHttpContext();
            string sessionId = httpContext?.Request.Query["sessionId"].ToString() ?? string.Empty;
            string primaryLang = httpContext?.Request.Query["primaryLang"].ToString() ?? string.Empty;
            string secondaryLang = httpContext?.Request.Query["secondaryLang"].ToString() ?? string.Empty;

            _logger.LogInformation("📋 Connection parameters - SessionId: {SessionId}, Primary: {Primary}, Secondary: {Secondary}", 
                sessionId, primaryLang, secondaryLang);
            Console.WriteLine($"📋 CONSOLE: Connection parameters - SessionId: {sessionId}, Primary: {primaryLang}, Secondary: {secondaryLang}");

            // ✅ PURE DOMAIN ARCHITECTURE: Create session via Command
            Console.WriteLine($"🔥 CONSOLE: About to create session for {Context.ConnectionId}");
            await _mediator.Send(new StartSessionCommand(Context.ConnectionId, sessionId, primaryLang, secondaryLang));
            Console.WriteLine($"✅ CONSOLE: Session created successfully for {Context.ConnectionId}");

            // Get the created session from repository
            var session = await _sessionRepository.GetByConnectionIdAsync(Context.ConnectionId, CancellationToken.None);
            if (session == null)
            {
                throw new InvalidOperationException($"Failed to create session for {Context.ConnectionId}");
            }
            
            _logger.LogInformation("✅ Conversation session created: {SessionId}", session.SessionId);
            Console.WriteLine($"✅ CONSOLE: Conversation session created: {session.SessionId}");

            // 🎵 DEBUG: Start audio test collection immediately when session starts
            _audioTestCollector.StartCollection(session.SessionId);

            // Send connection confirmation
            await Clients.Caller.ReceiveTranscription("Connected successfully", "system", true);

            // Delegate language detection to service
            Console.WriteLine($"🔍 CONSOLE: About to call GetOrDetectLanguageAsync for {Context.ConnectionId}");
            var languageResult = await _languageService.GetOrDetectLanguageAsync(
                session.SessionId, 
                new[] { session.PrimaryLanguage, session.SecondaryLanguage ?? "en-US" },
                session);
            Console.WriteLine($"🔍 CONSOLE: GetOrDetectLanguageAsync completed for {Context.ConnectionId}");

            // 🔍 DEBUG: Log the language detection result
            _logger.LogInformation("🎯 Language detection result - IsKnown: {IsKnown}, RequiresDetection: {RequiresDetection}, Language: {Language}", 
                languageResult.IsKnown, languageResult.RequiresDetection, languageResult.Language);
            Console.WriteLine($"🎯 CONSOLE: Language detection result - IsKnown: {languageResult.IsKnown}, RequiresDetection: {languageResult.RequiresDetection}, Language: {languageResult.Language}");

            // 🌍 SIMPLIFIED: Always use Google Auto-Detection for all cases
            var candidateLanguages = languageResult.IsKnown 
                ? new[] { languageResult.Language } 
                : languageResult.CandidateLanguages;

            if (languageResult.IsKnown)
            {
                await Clients.Caller.ReceiveDominantLanguageDetected(languageResult.Language);
            }

            _logger.LogInformation("🌍 Starting Google auto-detection for {ConnectionId} with candidates: [{Languages}]", 
                Context.ConnectionId, string.Join(", ", candidateLanguages));
            Console.WriteLine($"🌍 CONSOLE: Starting Google auto-detection for {Context.ConnectionId} with candidates: [{string.Join(", ", candidateLanguages)}]");
                
            _ = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"🌍 CONSOLE: Inside Task.Run - calling StartAutoLanguageDetectionAsync for {connectionId}");
                    
                    await _sttProcessor.StartAutoLanguageDetectionAsync(
                        connectionId, candidateLanguages, _hubCancellationTokenSource.Token);
                    Console.WriteLine($"🏁 CONSOLE: StartAutoLanguageDetectionAsync completed for {connectionId}");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🛑 Google auto-detection cancelled for {ConnectionId}", connectionId);
                    Console.WriteLine($"🛑 CONSOLE: Google auto-detection cancelled for {connectionId}");
                }
                catch (ObjectDisposedException ex)
                {
                    _logger.LogInformation("🛑 Google auto-detection stopped - resources disposed for {ConnectionId}", connectionId);
                    Console.WriteLine($"🛑 CONSOLE: Google auto-detection stopped - resources disposed for {connectionId}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Google auto-detection failed for {ConnectionId}", connectionId);
                    Console.WriteLine($"❌ CONSOLE: Google auto-detection failed for {connectionId}: {ex.Message}");
                    Console.WriteLine($"❌ CONSOLE: Stack trace: {ex.StackTrace}");
                }
            });
            
            Console.WriteLine($"🌍 CONSOLE: Task.Run created for Google auto-detection for {connectionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ CONSOLE: CRITICAL ERROR in OnConnectedAsync for {Context.ConnectionId}: {ex.Message}");
            Console.WriteLine($"❌ CONSOLE: Stack trace: {ex.StackTrace}");
            _logger.LogError(ex, "❌ Critical error in OnConnectedAsync for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError($"Connection failed: {ex.Message}");
        }
        
        Console.WriteLine($"🏁 CONSOLE: OnConnectedAsync COMPLETED for {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogError(exception, "❌ Client {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("👋 Client {ConnectionId} disconnected gracefully", Context.ConnectionId);
        }
        
        // 🎵 DEBUG: Stop and save audio test collection when client disconnects
        try
        {
            _audioTestCollector.StopAndSave(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error stopping audio test collector for {ConnectionId}", Context.ConnectionId);
        }
        
        // Cancel all pending operations for this hub
        if (!_hubCancellationTokenSource.Token.IsCancellationRequested)
        {
            _hubCancellationTokenSource.Cancel();
        }
        
        try
        {
            // ✅ PURE DOMAIN: End session via repository
            var session = await _sessionRepository.GetByConnectionIdAsync(Context.ConnectionId, CancellationToken.None);
            if (session != null)
            {
                // End session and close audio stream
                session.EndSession(A3ITranslator.Application.Domain.Entities.SessionStatus.Completed);
                await _sessionRepository.SaveAsync(session, CancellationToken.None);
            }

            // 🧹 Cleanup utterance collectors to prevent memory leaks
            _sttProcessor.CleanupConnection(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during cleanup for {ConnectionId}", Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    protected new virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    if (!_hubCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        _hubCancellationTokenSource.Cancel();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error cancelling token during dispose");
                }
                finally
                {
                    _hubCancellationTokenSource?.Dispose();
                }
            }
            _disposed = true;
        }
        
        // Call base dispose
        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task UploadAudioStream(ChannelReader<string> stream) // ✅ Now ChannelReader will be recognized
    {
        try
        {
            _logger.LogInformation("🎵 Starting audio stream for {ConnectionId}", Context.ConnectionId);
            
            while (await stream.WaitToReadAsync())
            {
                while (stream.TryRead(out var base64Chunk))
                {
                    await _mediator.Send(new ProcessAudioChunkCommand(Context.ConnectionId, base64Chunk));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in audio stream for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError("Stream interrupted");
        }
    }

    // Unified method that can handle both enhanced payload and simple base64 string
    public async Task SendAudioChunk(object audioPayload)
    {
        try
        {
            string audioData = "";
            string sessionId = "unknown";
            string chunkId = "unknown";

            // Determine if this is an enhanced payload or simple string
            if (audioPayload is string simpleBase64)
            {
                // Simple base64 string (backward compatibility)
                audioData = simpleBase64;
                _logger.LogInformation("🎵 Simple audio chunk received for {ConnectionId}, size: {Size}", 
                    Context.ConnectionId, audioData?.Length ?? 0);
            }
            else if (audioPayload is System.Text.Json.JsonElement jsonElement)
            {
                // Handle JsonElement from SignalR JSON deserialization
                _logger.LogInformation("🎵 JsonElement received for {ConnectionId}, ValueKind: {ValueKind}", 
                    Context.ConnectionId, jsonElement.ValueKind);

                if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    // It's a JSON string containing base64 audio data
                    audioData = jsonElement.GetString() ?? "";
                    _logger.LogInformation("🎵 JSON string audio chunk received for {ConnectionId}, size: {Size}", 
                        Context.ConnectionId, audioData?.Length ?? 0);
                }
                else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // It's a JSON object with enhanced payload structure
                    try
                    {
                        if (jsonElement.TryGetProperty("audioData", out var audioDataProp))
                        {
                            audioData = audioDataProp.ValueKind == System.Text.Json.JsonValueKind.String 
                                ? (audioDataProp.GetString() ?? "") 
                                : "";
                        }
                        
                        if (jsonElement.TryGetProperty("metadata", out var metadataProp))
                        {
                            if (metadataProp.TryGetProperty("sessionId", out var sessionIdProp))
                            {
                                sessionId = sessionIdProp.ValueKind == System.Text.Json.JsonValueKind.String 
                                    ? (sessionIdProp.GetString() ?? "unknown") 
                                    : "unknown";
                            }
                            if (metadataProp.TryGetProperty("chunkId", out var chunkIdProp))
                            {
                                chunkId = chunkIdProp.ValueKind == System.Text.Json.JsonValueKind.String 
                                    ? (chunkIdProp.GetString() ?? "unknown") 
                                    : "unknown";
                            }
                        }
                        
                        _logger.LogInformation("🎵 Enhanced JSON audio chunk received for {ConnectionId}, ChunkId: {ChunkId}, Session: {SessionId}", 
                            Context.ConnectionId, chunkId, sessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to parse JsonElement enhanced payload for {ConnectionId}", Context.ConnectionId);
                        audioData = "";
                    }
                }
                else
                {
                    _logger.LogWarning("❌ Unexpected JsonElement ValueKind {ValueKind} for {ConnectionId}", 
                        jsonElement.ValueKind, Context.ConnectionId);
                    audioData = "";
                }
            }
            else
            {
                // Enhanced payload with metadata (dynamic object)
                try
                {
                    dynamic enhancedPayload = audioPayload;
                    audioData = enhancedPayload?.audioData?.ToString() ?? "";
                    var metadata = enhancedPayload?.metadata;
                    sessionId = metadata?.sessionId?.ToString() ?? "unknown";
                    chunkId = metadata?.chunkId?.ToString() ?? "unknown";
                    
                    _logger.LogInformation("🎵 Dynamic enhanced audio chunk received for {ConnectionId}, ChunkId: {ChunkId}, Session: {SessionId}", 
                        Context.ConnectionId, chunkId, sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to parse dynamic enhanced payload for {ConnectionId}", Context.ConnectionId);
                    audioData = "";
                }
            }

            if (_hubCancellationTokenSource.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Audio chunk ignored - hub is disposing for {ConnectionId}", Context.ConnectionId);
                return;
            }

            if (string.IsNullOrEmpty(audioData))
            {
                _logger.LogWarning("❌ Empty audio data received for {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.ReceiveError("Empty audio data");
                return;
            }

            // // 🎵 DEBUG: Intercept audio for testing
            // try
            // {
            //     if (isEnhancedPayload)
            //     {
            //         await _audioTestCollector.AddChunk(audioPayload);
            //         Console.WriteLine($"🎵 AUDIO TEST: Captured enhanced chunk for {Context.ConnectionId} - ChunkId: {chunkId}");
            //     }
            //     else
            //     {
            //         await _audioTestCollector.AddChunk(audioData);
            //         Console.WriteLine($"🎵 AUDIO TEST: Captured simple chunk for {Context.ConnectionId} - {audioData.Length} chars");
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Console.WriteLine($"❌ AUDIO TEST: Failed to capture chunk for {Context.ConnectionId}: {ex.Message}");
            // }

            // Process the audio data using common logic
            await ProcessAudioData(audioData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing audio chunk for {ConnectionId}", Context.ConnectionId);
            try
            {
                await Clients.Caller.ReceiveError($"Chunk processing failed: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Hub disposed while sending error for {ConnectionId}", Context.ConnectionId);
            }
        }
    }

    // // Original method for backward compatibility (simple base64 string)
    // public async Task SendAudioChunk(string base64AudioChunk)
    // {
    //     try
    //     {
    //         // 🔍 CRITICAL: Add detailed logging to track audio chunks
    //         _logger.LogInformation("🎵 Audio chunk received for {ConnectionId}, size: {Size}", 
    //             Context.ConnectionId, base64AudioChunk?.Length ?? 0);

    //         if (_hubCancellationTokenSource.Token.IsCancellationRequested)
    //         {
    //             _logger.LogWarning("Audio chunk ignored - hub is disposing for {ConnectionId}", Context.ConnectionId);
    //             return;
    //         }

    //         if (string.IsNullOrEmpty(base64AudioChunk))
    //         {
    //             _logger.LogWarning("❌ Empty audio chunk received for {ConnectionId}", Context.ConnectionId);
    //             await Clients.Caller.ReceiveError("Empty audio chunk");
    //             return;
    //         }

    //         // 🎵 DEBUG: FIRST - Intercept audio for testing with base64 string
    //         try
    //         {
    //             _audioTestCollector.AddChunk(base64AudioChunk);
    //             Console.WriteLine($"🎵 AUDIO TEST: Captured base64 chunk for {Context.ConnectionId} - {base64AudioChunk.Length} chars");
    //         }
    //         catch (Exception ex)
    //         {
    //             Console.WriteLine($"❌ AUDIO TEST: Failed to capture chunk for {Context.ConnectionId}: {ex.Message}");
    //         }

    //         // � CRITICAL FIX: Convert WebM container to PCM before sending to STT services
    //         string processedBase64AudioChunk;
    //         try
    //         {
    //             Console.WriteLine($"🔧 CONVERSION PIPELINE: Processing chunk for {Context.ConnectionId}");
                
    //             // Decode base64 to get WebM container bytes
    //             byte[] webmBytes = Convert.FromBase64String(base64AudioChunk);
    //             Console.WriteLine($"   📊 Decoded {webmBytes.Length} bytes from base64");
    //             Console.WriteLine($"   � Header bytes: [{string.Join(", ", webmBytes.Take(10))}]");
                
    //             // Detect if this is a container format that needs conversion
    //             if (IsContainerFormat(webmBytes))
    //             {
    //                 Console.WriteLine($"🔄 CONTAINER DETECTED: Converting WebM/container to PCM for {Context.ConnectionId}");
                    
    //                 // Convert WebM container to raw PCM using FFmpeg
    //                 byte[] pcmBytes = await ConvertWebmToPcm(webmBytes);
                    
    //                 Console.WriteLine($"✅ CONVERSION SUCCESS: {webmBytes.Length} WebM bytes → {pcmBytes.Length} PCM bytes");
    //                 Console.WriteLine($"   🎵 PCM header: [{string.Join(", ", pcmBytes.Take(10))}]");
                    
    //                 // Re-encode clean PCM as base64 for the orchestrator
    //                 processedBase64AudioChunk = Convert.ToBase64String(pcmBytes);
    //                 Console.WriteLine($"📤 CONVERSION COMPLETE: Sending clean PCM to orchestrator");
    //             }
    //             else
    //             {
    //                 Console.WriteLine($"✅ RAW PCM: No conversion needed for {Context.ConnectionId}");
    //                 processedBase64AudioChunk = base64AudioChunk; // Use original
    //             }
    //         }
    //         catch (Exception conversionEx)
    //         {
    //             Console.WriteLine($"❌ CONVERSION ERROR: {conversionEx.Message} for {Context.ConnectionId}");
    //             Console.WriteLine($"   🔄 Falling back to original audio chunk");
    //             processedBase64AudioChunk = base64AudioChunk; // Fallback to original
    //         }

    //         // 📡 Send the processed (converted or original) audio to the orchestrator
    //         _logger.LogDebug("📡 Processing audio chunk for {ConnectionId}", Context.ConnectionId);
    //         await _orchestrator.ProcessAudioChunkAsync(Context.ConnectionId, processedBase64AudioChunk);
    //         _logger.LogDebug("✅ Audio chunk processed successfully for {ConnectionId}", Context.ConnectionId);
    //     }
    //     catch (ObjectDisposedException)
    //     {
    //         _logger.LogWarning("Hub disposed while processing audio chunk for {ConnectionId}", Context.ConnectionId);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "❌ Error processing chunk for {ConnectionId}", Context.ConnectionId);
    //         try
    //         {
    //             await Clients.Caller.ReceiveError($"Chunk processing failed: {ex.Message}");
    //         }
    //         catch (ObjectDisposedException)
    //         {
    //             _logger.LogWarning("Hub disposed while sending error for {ConnectionId}", Context.ConnectionId);
    //         }
    //     }
    // }

    /// <summary>
    /// Called when STT times out and returns whatever transcript it has
    /// This handles the same flow as CommitUtterance but from timeout
    /// </summary>
    public async Task HandleSttTimeout(dynamic timeoutResult)
    {
        try
        {
            _logger.LogInformation("⏰ STT timeout occurred for {ConnectionId}", Context.ConnectionId);
            await HandleTranscriptionResult(timeoutResult, isFromCommit: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling STT timeout for {ConnectionId}", Context.ConnectionId);
            await SendProcessingError("Timeout error - please try again");
        }
    }

    public async Task CommitUtterance()
    {
        try
        {
            if (_hubCancellationTokenSource.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Commit utterance ignored - hub is disposing for {ConnectionId}", Context.ConnectionId);
                return;
            }

            _logger.LogInformation("✅ Committing utterance for {ConnectionId}", Context.ConnectionId);
            
            // ✅ STEP 1: Signal to STT processor to finalize gracefully
            var session = await _sessionRepository.GetByConnectionIdAsync(Context.ConnectionId, CancellationToken.None);
            if (session != null)
            {
                // Close audio channel to signal "no more audio coming"
                session.AudioStreamChannel.Writer.Complete();
                _logger.LogInformation("🔚 Audio channel completed for finalization for {ConnectionId}", Context.ConnectionId);
                
                // ✅ STEP 2: Wait for STT processing to complete and populate FinalTranscript
                _logger.LogInformation("⏰ Waiting for STT processing to complete for {ConnectionId}...", Context.ConnectionId);
                
                var maxWaitTime = TimeSpan.FromSeconds(8);
                var checkInterval = TimeSpan.FromMilliseconds(500);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // Poll until we have a transcript or timeout
                while (stopwatch.Elapsed < maxWaitTime)
                {
                    await Task.Delay(checkInterval);
                    
                    // Refresh session to check if FinalTranscript has been populated
                    var refreshedSession = await _sessionRepository.GetByConnectionIdAsync(Context.ConnectionId, CancellationToken.None);
                    if (refreshedSession != null && !string.IsNullOrWhiteSpace(refreshedSession.FinalTranscript))
                    {
                        _logger.LogInformation("✅ STT processing completed with transcript: \"{Transcript}\" (took {ElapsedMs}ms)", 
                            refreshedSession.FinalTranscript.Trim(), stopwatch.ElapsedMilliseconds);
                        break;
                    }
                    
                    if (stopwatch.Elapsed.TotalSeconds % 2 == 0) // Log every 2 seconds
                    {
                        _logger.LogInformation("⏰ Still waiting for STT processing... ({ElapsedMs}ms elapsed)", stopwatch.ElapsedMilliseconds);
                    }
                }
                
                if (stopwatch.Elapsed >= maxWaitTime)
                {
                    _logger.LogWarning("⚠️ STT processing timeout after {TimeoutMs}ms for {ConnectionId}", maxWaitTime.TotalMilliseconds, Context.ConnectionId);
                }
                
                _logger.LogInformation("⏰ Finalization delay completed, proceeding with commit for {ConnectionId}", Context.ConnectionId);
            }
            
            // ✅ STEP 3: Now commit with the final transcript
            var result = await _mediator.Send(new CommitUtteranceCommand(Context.ConnectionId));

            await HandleTranscriptionResult(result, isFromCommit: true);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while committing utterance for {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in CommitUtterance for {ConnectionId}", Context.ConnectionId);
            await SendProcessingError("System error occurred - please try again");
        }
    }

    /// <summary>
    /// Handle transcription results from either CommitUtterance or STT timeout
    /// </summary>
    private async Task HandleTranscriptionResult(dynamic result, bool isFromCommit = false)
    {
        try
        {
            bool success = result.Success;
            string transcript = result.Transcript ?? "";
            string errorMessage = result.ErrorMessage ?? "";

            if (!success)
            {
                _logger.LogWarning("Transcription failed: {Message} for {ConnectionId}", errorMessage, Context.ConnectionId);
                await SendAudioReceptionError("Could not process audio - please speak again");
                return;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _logger.LogInformation("Empty transcript received for {ConnectionId}", Context.ConnectionId);
                await SendAudioReceptionError("No speech detected - please speak again");
                return;
            }

            // 🎯 SUCCESS: Audio received and transcribed!
            _logger.LogInformation("✅ Audio received and transcribed: {Transcript} for {ConnectionId}", 
                transcript, Context.ConnectionId);
            
            // 📡 CRITICAL: Send audio reception acknowledgment
            // This tells frontend: "Stop recording, we got your speech"
            await SendAudioReceptionAck("Audio received - generating response...");
            
            // ✅ Processing continues via Events (GenAI, TTS)
            // Event handlers will send status updates and final completion
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling transcription result for {ConnectionId}", Context.ConnectionId);
            await SendProcessingError("Processing error - please try again");
        }
    }

    /// <summary>
    /// Send audio reception acknowledgment - stops recording, confirms processing started
    /// </summary>
    private async Task SendAudioReceptionAck(string message)
    {
        try
        {
            await Clients.Caller.ReceiveAudioReceptionAck(message);
            _logger.LogInformation("📤 Sent audio reception ACK to {ConnectionId}: {Message}", Context.ConnectionId, message);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while sending audio reception ACK for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Send audio reception error - stops recording, asks to try again
    /// </summary>
    private async Task SendAudioReceptionError(string errorMessage)
    {
        try
        {
            await Clients.Caller.ReceiveAudioReceptionError(errorMessage);
            _logger.LogInformation("📤 Sent audio reception ERROR to {ConnectionId}: {Message}", Context.ConnectionId, errorMessage);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while sending audio reception error for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Send processing status update
    /// </summary>
    public async Task SendProcessingStatus(string status)
    {
        try
        {
            await Clients.Caller.ReceiveProcessingStatus(status);
            _logger.LogInformation("📤 Sent processing status to {ConnectionId}: {Status}", Context.ConnectionId, status);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while sending processing status for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Send processing error - shows error but keeps session active
    /// </summary>
    public async Task SendProcessingError(string errorMessage)
    {
        try
        {
            await Clients.Caller.ReceiveProcessingError(errorMessage);
            _logger.LogInformation("📤 Sent processing error to {ConnectionId}: {Message}", Context.ConnectionId, errorMessage);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while sending processing error for {ConnectionId}", Context.ConnectionId);
        }
    }

    public async Task TestConnection()
    {
        try
        {
            _logger.LogInformation("🧪 Testing connection for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveTranscription("Connection test successful", "system", true);
            _logger.LogInformation("✅ Test response sent to {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Test connection failed for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Test method to verify audio chunk reception
    /// </summary>
    public async Task TestAudioChunk()
    {
        try
        {
            _logger.LogInformation("🎵 TestAudioChunk called for {ConnectionId}", Context.ConnectionId);
            await SendAudioChunk("dGVzdA=="); // "test" in base64
            await Clients.Caller.ReceiveTranscription("Audio chunk test completed", "system", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TestAudioChunk failed for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Detect if audio bytes contain a container format (WebM, MP4, etc.)
    /// </summary>
    private bool IsContainerFormat(byte[] audioBytes)
    {
        if (audioBytes.Length < 4)
            return false;

        // Check for WebM signature (EBML header)
        if (audioBytes.Length >= 4 && audioBytes[0] == 0x1A && audioBytes[1] == 0x45 && audioBytes[2] == 0xDF && audioBytes[3] == 0xA3)
        {
            return true;
        }

        // Check for MP4/M4A signature (ftyp box)
        if (audioBytes.Length >= 8 && audioBytes[4] == 0x66 && audioBytes[5] == 0x74 && audioBytes[6] == 0x79 && audioBytes[7] == 0x70)
        {
            return true;
        }

        // Check for OGG signature
        var headerStr = System.Text.Encoding.ASCII.GetString(audioBytes.Take(4).ToArray());
        if (headerStr == "OggS")
        {
            return true;
        }

        // Check for MP3 frame header (starts with 0xFF, second byte has specific pattern)
        if (audioBytes.Length >= 2 && audioBytes[0] == 0xFF && (audioBytes[1] & 0xE0) == 0xE0)
        {
            return true;
        }

        // Check if it's already WAV (should not be converted)
        if (headerStr == "RIFF" && audioBytes.Length >= 12)
        {
            var waveStr = System.Text.Encoding.ASCII.GetString(audioBytes.Skip(8).Take(4).ToArray());
            if (waveStr == "WAVE")
            {
                return false; // Already proper format
            }
        }

        return false; // Assume raw PCM
    }

    // Common audio processing logic
    private async Task ProcessAudioData(string base64AudioData)
    {
        try
        {
            Console.WriteLine($"🎵 DIRECT WEBM: Processing chunk for {Context.ConnectionId}");
            
            // Decode base64 to get raw WebM/Opus bytes
            byte[] webmBytes = Convert.FromBase64String(base64AudioData);
            Console.WriteLine($"   📊 Decoded {webmBytes.Length} bytes from base64");
            Console.WriteLine($"   🎵 Header bytes: [{string.Join(", ", webmBytes.Take(10))}]");
            
            // 🎵 CRITICAL: Send WebM data directly to orchestrator - no conversion!
            // The orchestrator will handle routing to appropriate STT services:
            // - Google STT: Use WebM directly (TranscribeWebMStreamAsync)
            // - Azure STT: Convert to PCM if needed
            Console.WriteLine($"📤 DIRECT WEBM: Sending {webmBytes.Length} bytes directly to orchestrator");
            
            // ✅ CLEAN ARCHITECTURE: Use Command
            await _mediator.Send(new ProcessAudioChunkCommand(Context.ConnectionId, base64AudioData));
            
            Console.WriteLine($"✅ DIRECT WEBM: Successfully sent to orchestrator for {Context.ConnectionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ DIRECT WEBM ERROR: {ex.Message} for {Context.ConnectionId}");
            _logger.LogError(ex, "❌ Error in direct WebM processing for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Convert WebM container to raw PCM using FFmpeg
    /// </summary>
    private async Task<byte[]> ConvertWebmToPcm(byte[] webmBytes)
    {
        try
        {
            // Use the FFmpeg conversion helper to convert to WAV
            var tempWavFile = await AudioConversionHelper.ConvertToWavWithFFmpeg(webmBytes, CancellationToken.None);
            
            // Read the converted WAV file
            var wavData = await File.ReadAllBytesAsync(tempWavFile);
            
            // Clean up the temporary file
            try { File.Delete(tempWavFile); } catch { }
            
            // Extract just the PCM data (skip WAV header)
            byte[] pcmData = ExtractPcmFromWav(wavData);
            
            return pcmData;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WebM to PCM conversion failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract PCM data from WAV file bytes
    /// </summary>
    private byte[] ExtractPcmFromWav(byte[] wavData)
    {
        if (wavData.Length <= 44)
        {
            throw new InvalidOperationException($"WAV file too small ({wavData.Length} bytes)");
        }

        // Find the "data" chunk in the WAV file
        for (int i = 12; i < wavData.Length - 8; i++)
        {
            if (wavData[i] == 'd' && wavData[i + 1] == 'a' && wavData[i + 2] == 't' && wavData[i + 3] == 'a')
            {
                // Found data chunk, get the size
                int dataSize = BitConverter.ToInt32(wavData, i + 4);
                int dataStart = i + 8;
                
                if (dataStart + dataSize <= wavData.Length)
                {
                    // Extract PCM data
                    var pcmData = new byte[dataSize];
                    Buffer.BlockCopy(wavData, dataStart, pcmData, 0, dataSize);
                    return pcmData;
                }
                break;
            }
        }
        
        // Fallback: return data after standard 44-byte header
        if (wavData.Length > 44)
        {
            var pcmData = new byte[wavData.Length - 44];
            Buffer.BlockCopy(wavData, 44, pcmData, 0, pcmData.Length);
            return pcmData;
        }
        
        throw new InvalidOperationException("Cannot extract PCM data from WAV file");
    }

    // ✅ NEW: Streaming translation methods
    /// <summary>
    /// Start streaming translation process
    /// </summary>
    public async Task StartStreamingTranslation(string transcriptionText)
    {
        try
        {
            var connectionId = Context.ConnectionId;
            _logger.LogInformation("🔄 StartStreamingTranslation called for {ConnectionId}", connectionId);

            // Get session
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                await Clients.Caller.ReceiveError("Session not found");
                return;
            }

            // TODO: Implement streaming translation via MediatR command instead of legacy orchestrator
            // This functionality should be handled via the Domain/Application layer
            _logger.LogInformation("✅ StartStreamingTranslation called - delegating to Domain handlers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in StartStreamingTranslation for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError($"Streaming translation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancel active streaming operation
    /// </summary>
    public async Task CancelStreaming()
    {
        try
        {
            var connectionId = Context.ConnectionId;
            _logger.LogInformation("🛑 CancelStreaming called for {ConnectionId}", connectionId);

            // Get session to find session ID
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session != null)
            {
                // Cancel via streaming orchestrator (will be implemented)
                // await _streamingOrchestrator.CancelStreamingAsync(session.SessionId);
            }

            await Clients.Caller.ReceiveOperationCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in CancelStreaming for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError($"Cancel operation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Confirm cycle completion and readiness for next audio
    /// </summary>
    public async Task ConfirmReadyForNext()
    {
        try
        {
            var connectionId = Context.ConnectionId;
            _logger.LogInformation("✅ ConfirmReadyForNext called for {ConnectionId}", connectionId);

            // Get session
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                await Clients.Caller.ReceiveError("Session not found");
                return;
            }

            // Reset session state for next audio cycle
            // This could involve clearing buffers, resetting state, etc.
            
            await Clients.Caller.ReceiveCycleCompletion(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in ConfirmReadyForNext for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError($"Ready confirmation failed: {ex.Message}");
        }
    }
}