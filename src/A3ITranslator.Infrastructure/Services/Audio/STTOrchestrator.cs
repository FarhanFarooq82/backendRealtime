using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// STT Orchestrator with single language processing
/// Simplified architecture using single language instead of multi-candidate detection
/// </summary>
public class STTOrchestrator : IStreamingSTTService
{
    private readonly ILogger<STTOrchestrator> _logger;
    private readonly GoogleStreamingSTTService _googleSTT;
    private readonly AzureStreamingSTTService _azureSTT;

    public STTOrchestrator(
        ILogger<STTOrchestrator> logger,
        GoogleStreamingSTTService googleSTT,
        AzureStreamingSTTService azureSTT)
    {
        _logger = logger;
        _googleSTT = googleSTT;
        _azureSTT = azureSTT;

        _logger.LogInformation("🔧 STT Orchestrator initialized with single language processing");
    }

    /// <summary>
    /// Process audio stream with the specified language for transcription
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> ProcessStreamAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var googleSucceeded = false;

        // 🎯 GOOGLE STT PHASE (Real-time consumption)
        IAsyncEnumerator<TranscriptionResult>? googleEnumerator = null;
        try
        {
            _logger.LogDebug("🔍 STT Orchestrator: Processing with Google STT for language: {Language}", language);
            googleEnumerator = _googleSTT.ProcessStreamAsync(audioStream, language, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT Orchestrator: Google STT initialization failed for {Language}", language);
        }

        if (googleEnumerator != null)
        {
            while (true)
            {
                TranscriptionResult? result = null;
                try
                {
                    if (!await googleEnumerator.MoveNextAsync()) break;
                    result = googleEnumerator.Current;
                    if (result.IsFinal) googleSucceeded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ STT Orchestrator: Google STT stream error for {Language}", language);
                    break;
                }

                if (result != null) yield return result;
            }
            await googleEnumerator.DisposeAsync();
        }

        // 🎯 AZURE FALLBACK PHASE (If Google didn't produce a final result)
        if (!googleSucceeded)
        {
            _logger.LogWarning("🔄 STT Orchestrator: Falling back to Azure STT for language: {Language}", language);
            IAsyncEnumerator<TranscriptionResult>? azureEnumerator = null;
            
            try
            {
                azureEnumerator = _azureSTT.ProcessStreamAsync(audioStream, language, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ STT Orchestrator: Azure STT fallback initialization failed for {Language}", language);
            }

            if (azureEnumerator != null)
            {
                while (true)
                {
                    TranscriptionResult? result = null;
                    try
                    {
                        if (!await azureEnumerator.MoveNextAsync()) break;
                        result = azureEnumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ STT Orchestrator: Azure STT fallback stream error for {Language}", language);
                        break;
                    }

                    if (result != null) yield return result;
                }
                await azureEnumerator.DisposeAsync();
            }
            else
            {
                // Last resort error result
                yield return new TranscriptionResult
                {
                    Text = $"[STT Processing Failed for {language}]",
                    Language = language,
                    IsFinal = true,
                    Confidence = 0.0,
                    Timestamp = TimeSpan.Zero
                };
            }
        }
    }
}