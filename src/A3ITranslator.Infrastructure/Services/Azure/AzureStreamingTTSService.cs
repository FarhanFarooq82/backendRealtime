using System.Runtime.CompilerServices;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A3ITranslator.Infrastructure.Services.Azure;

public class AzureStreamingTTSService : IStreamingTTSService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<AzureStreamingTTSService> _logger;
    private readonly IRealtimeNotificationService _notificationService;

    public AzureStreamingTTSService(
        IOptions<ServiceOptions> options, 
        ILogger<AzureStreamingTTSService> logger,
        IRealtimeNotificationService notificationService)
    {
        _options = options.Value;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task SynthesizeAndNotifyAsync(string connectionId, string text, string language, CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in SynthesizeStreamAsync(text, language, "", cancellationToken))
        {
            await _notificationService.SendTTSAudioSegmentAsync(connectionId, new Application.DTOs.Common.TTSAudioSegment
            {
                AudioData = Convert.ToBase64String(chunk.AudioData),
                AssociatedText = chunk.AssociatedText,
                IsFirstChunk = chunk.IsFirstChunk,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                ConversationItemId = "tts-" + Guid.NewGuid().ToString()[..8]
            });
        }
    }

    public async IAsyncEnumerable<TTSChunk> SynthesizeStreamAsync(
        string text, 
        string language, 
        string voiceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var config = SpeechConfig.FromSubscription(_options.Azure.SpeechKey, _options.Azure.SpeechRegion);
        config.SpeechSynthesisLanguage = language;
        if (!string.IsNullOrEmpty(voiceName))
        {
            config.SpeechSynthesisVoiceName = voiceName;
        }

        // ✅ Set output format for streaming
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

        var audioChunks = new List<byte[]>();
        SpeechSynthesisResult? result = null;

        try
        {
            using var synthesizer = new SpeechSynthesizer(config, null); // null output for in-memory
            
            _logger.LogDebug("Starting Azure TTS synthesis for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));

            // ✅ Subscribe to synthesizing events for streaming
            synthesizer.Synthesizing += (sender, e) =>
            {
                if (e.Result.AudioData?.Length > 0)
                {
                    var chunk = new byte[e.Result.AudioData.Length];
                    e.Result.AudioData.CopyTo(chunk, 0);
                    audioChunks.Add(chunk);
                    
                    _logger.LogTrace("Azure TTS audio chunk received: {Size} bytes", chunk.Length);
                }
            };

            // ✅ Start synthesis with result awaiting
            result = await synthesizer.SpeakTextAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Azure TTS synthesis: {Message}", ex.Message);
            yield break; // Exit gracefully on error
        }

        // ✅ Process results outside try-catch to allow yielding
        if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            _logger.LogDebug("Azure TTS synthesis completed successfully, yielding {Count} chunks", audioChunks.Count);

            // ✅ Yield accumulated audio chunks
            for (int i = 0; i < audioChunks.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new TTSChunk 
                { 
                    AudioData = audioChunks[i],
                    BoundaryType = i == audioChunks.Count - 1 ? "end" : "chunk",
                    IsFirstChunk = i == 0,
                    ChunkIndex = i,
                    TotalChunks = audioChunks.Count,
                    AssociatedText = text
                };
            }
        }
        else if (result?.Reason == ResultReason.Canceled)
        {
            var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
            _logger.LogError("Azure TTS synthesis cancelled: {Reason}, {ErrorDetails}", 
                cancellationDetails.Reason, cancellationDetails.ErrorDetails);
        }

        // ✅ Clean up result
        result?.Dispose();
    }
}
