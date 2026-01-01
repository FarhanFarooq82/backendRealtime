using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Interfaces;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Helpers;
using System.Threading.Channels;

namespace A3ITranslator.API.Hubs;

/// <summary>
/// Clean SignalR Hub - no infrastructure dependencies
/// </summary>
public class AudioConversationHub : Hub<IAudioClient>, IDisposable
{
    private readonly ILogger<AudioConversationHub> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly ILanguageDetectionService _languageService;
    private readonly ISttProcessor _sttProcessor;
    private readonly IRealtimeAudioOrchestrator _orchestrator;
    private readonly AudioTestCollector _audioTestCollector; // 🎵 DEBUG: Audio test collector
    private readonly CancellationTokenSource _hubCancellationTokenSource = new();
    private bool _disposed = false;

    // ✅ Clean constructor - all dependencies are abstractions
    public AudioConversationHub(
        ILogger<AudioConversationHub> logger,
        IConnectionManager connectionManager, // Still here for interface but we'll stop using it
        ISessionManager sessionManager,
        ILanguageDetectionService languageService,
        ISttProcessor sttProcessor,
        IRealtimeAudioOrchestrator orchestrator,
        AudioTestCollector audioTestCollector) // 🎵 DEBUG: Inject audio test collector
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _languageService = languageService;
        _sttProcessor = sttProcessor;
        _orchestrator = orchestrator;
        _audioTestCollector = audioTestCollector; // 🎵 DEBUG: Assign audio test collector
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            _logger.LogInformation("🔌 New SignalR connection: {ConnectionId}", Context.ConnectionId);
            
            var httpContext = Context.GetHttpContext();
            string sessionId = httpContext?.Request.Query["sessionId"].ToString() ?? string.Empty;
            string primaryLang = httpContext?.Request.Query["primaryLang"].ToString() ?? string.Empty;
            string secondaryLang = httpContext?.Request.Query["secondaryLang"].ToString() ?? string.Empty;

            _logger.LogInformation("📋 Connection parameters - SessionId: {SessionId}, Primary: {Primary}, Secondary: {Secondary}", 
                sessionId, primaryLang, secondaryLang);

            // 🔥 CRITICAL: Use the unified SessionManager to create/init the session
            var session = await _sessionManager.CreateSessionAsync(
                Context.ConnectionId, sessionId, primaryLang, secondaryLang);
            
            _logger.LogInformation("✅ Conversation session created: {SessionId}", session.SessionId);

            // 🎵 DEBUG: Start audio test collection immediately when session starts
            _audioTestCollector.StartCollection(session.SessionId);

            // Send connection confirmation
            await Clients.Caller.ReceiveTranscription("Connected successfully", "system", true);

            // Delegate language detection to service
            var languageResult = await _languageService.GetOrDetectLanguageAsync(
                session.SessionId, 
                new[] { session.PrimaryLanguage, session.SecondaryLanguage ?? "en-US" },
                session);

            // 🔍 DEBUG: Log the language detection result
            _logger.LogInformation("🎯 Language detection result - IsKnown: {IsKnown}, RequiresDetection: {RequiresDetection}, Language: {Language}", 
                languageResult.IsKnown, languageResult.RequiresDetection, languageResult.Language);

            // Start STT processing with proper exception handling and cancellation
            if (languageResult.IsKnown)
            {
                _logger.LogInformation("🎯 Using known language {Language} for {ConnectionId} - Starting background processing", 
                    languageResult.Language, Context.ConnectionId);
                    
                await Clients.Caller.ReceiveDominantLanguageDetected(languageResult.Language);
                
                // 🔥 CRITICAL FIX: Run in background so OnConnectedAsync can complete
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _sttProcessor.StartSingleLanguageProcessingAsync(
                            Context.ConnectionId, languageResult.Language, _hubCancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ STT processing failed for {ConnectionId}", Context.ConnectionId);
                    }
                }, _hubCancellationTokenSource.Token);
            }
            else if (languageResult.RequiresDetection)
            {
                _logger.LogInformation("🔍 Starting background language detection for {ConnectionId}", Context.ConnectionId);
                
                // 🔥 CRITICAL FIX: Run in background so OnConnectedAsync can complete
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _sttProcessor.StartMultiLanguageDetectionAsync(
                            Context.ConnectionId, languageResult.CandidateLanguages, _hubCancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Language detection failed for {ConnectionId}", Context.ConnectionId);
                    }
                }, _hubCancellationTokenSource.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Critical error in OnConnectedAsync for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError($"Connection failed: {ex.Message}");
        }
        
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
        
        // 🎵 DEBUG: Cleanup audio debug files for disconnected client
        _orchestrator.CleanupAudioDebug(Context.ConnectionId);
        
        // Cancel all pending operations for this hub
        if (!_hubCancellationTokenSource.Token.IsCancellationRequested)
        {
            _hubCancellationTokenSource.Cancel();
        }
        
        try
        {
            await _sessionManager.EndSessionAsync(Context.ConnectionId);
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
                    await _orchestrator.ProcessAudioChunkAsync(Context.ConnectionId, base64Chunk);
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
            bool isEnhancedPayload = false;

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
                        
                        isEnhancedPayload = true;
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
                    isEnhancedPayload = true;
                    
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
            
            var session = _sessionManager.GetSession(Context.ConnectionId);
            if (session == null)
            {
                await Clients.Caller.ReceiveError("Session not found in SessionManager");
                return;
            }

            string language = session.GetEffectiveLanguage();
            await _orchestrator.CommitAndProcessAsync(Context.ConnectionId, language);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Hub disposed while committing utterance for {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in CommitUtterance for {ConnectionId}", Context.ConnectionId);
            try
            {
                await Clients.Caller.ReceiveError($"Processing failed: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("Hub disposed while sending error for {ConnectionId}", Context.ConnectionId);
            }
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
            await _orchestrator.ProcessAudioChunkAsync(Context.ConnectionId, base64AudioData);
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
}