using System.Runtime.CompilerServices;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class StreamingTTSService : IStreamingTTSService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<StreamingTTSService> _logger;
    private readonly IRealtimeNotificationService _notificationService;

    public StreamingTTSService(
        IOptions<ServiceOptions> options, 
        ILogger<StreamingTTSService> logger,
        IRealtimeNotificationService notificationService)
    {
        _options = options.Value;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task<string> SynthesizeAndNotifyAsync(
        string connectionId, 
        string text, 
        string language, 
        string? speakerId = null,
        string estimatedGender = "Unknown", 
        bool isPremium = true, 
        CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in SynthesizeStreamAsync(text, language, "", cancellationToken))
        {
            await _notificationService.SendTTSAudioSegmentAsync(connectionId, new Application.DTOs.Common.TTSAudioSegment
            {
                AudioData = chunk.AudioData,
                AssociatedText = chunk.AssociatedText,
                IsFirstChunk = chunk.IsFirstChunk,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                ConversationItemId = "tts-" + Guid.NewGuid().ToString()[..8]
            });
        }
        return "Legacy-Standard";
    }

    public async IAsyncEnumerable<TTSChunk> SynthesizeStreamAsync(
        string text, 
        string language, 
        string voiceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) 
        {
            _logger.LogWarning("🔊 Azure TTS: Empty text provided for synthesis");
            yield break;
        }

        _logger.LogDebug("🔊 Azure TTS: Starting synthesis for text: {Text} (Voice: {Voice})", 
            text.Substring(0, Math.Min(text.Length, 50)), voiceName);

        var config = SpeechConfig.FromSubscription(_options.Azure.SpeechKey, _options.Azure.SpeechRegion);
        
        // ✅ Fix: Use proper output format for streaming to web browsers
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3);
        config.SpeechSynthesisLanguage = language;
        if (!string.IsNullOrEmpty(voiceName))
        {
            config.SpeechSynthesisVoiceName = voiceName;
        }

        using var synthesizer = new SpeechSynthesizer(config, null);

        _logger.LogDebug("🔊 Azure TTS: Config created, starting synthesis...");

        // ✅ Fix: Use SpeakTextAsync for data retrieval, not StartSpeakingTextAsync
        using var result = await synthesizer.SpeakTextAsync(text);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            _logger.LogDebug("🔊 Azure TTS: Synthesis completed, audio data length: {Length}", 
                result.AudioData?.Length ?? 0);

            // ✅ Fix: Use result.AudioData directly for streaming
            if (result.AudioData != null && result.AudioData.Length > 0)
            {
                const int chunkSize = 4096; // 4KB chunks for smooth streaming
                int totalChunks = (int)Math.Ceiling((double)result.AudioData.Length / chunkSize);
                
                _logger.LogDebug("🔊 Azure TTS: Streaming {TotalChunks} chunks", totalChunks);

                for (int i = 0; i < totalChunks; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("🔊 Azure TTS: Streaming cancelled at chunk {ChunkIndex}", i);
                        yield break;
                    }

                    int startIndex = i * chunkSize;
                    int length = Math.Min(chunkSize, result.AudioData.Length - startIndex);
                    
                    var chunk = new byte[length];
                    Array.Copy(result.AudioData, startIndex, chunk, 0, length);

                    _logger.LogTrace("🔊 Azure TTS: Yielding chunk {ChunkIndex}/{TotalChunks}, size: {Size}", 
                        i + 1, totalChunks, chunk.Length);

                    yield return new TTSChunk 
                    { 
                        AudioData = chunk,
                        AssociatedText = text,
                        BoundaryType = (i == totalChunks - 1) ? "end" : "chunk",
                        IsFirstChunk = (i == 0),
                        ChunkIndex = i,
                        TotalChunks = totalChunks
                    };

                    // Small delay for natural streaming feel
                    await Task.Delay(10, cancellationToken);
                }

                _logger.LogDebug("✅ Azure TTS: Streaming completed successfully");
            }
            else
            {
                _logger.LogWarning("⚠️ Azure TTS: Synthesis completed but no audio data received");
            }
        }
        else
        {
            _logger.LogError("❌ Azure TTS: Synthesis failed. Reason: {Reason}", result.Reason);
            
            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError("❌ Azure TTS: Cancellation details - Reason: {Reason}, ErrorCode: {ErrorCode}, ErrorDetails: {ErrorDetails}",
                    cancellation.Reason, cancellation.ErrorCode, cancellation.ErrorDetails);
            }
        }
    }
}
