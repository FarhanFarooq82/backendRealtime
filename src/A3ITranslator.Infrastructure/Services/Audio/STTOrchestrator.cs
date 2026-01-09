using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;


namespace A3ITranslator.Infrastructure.Services.Audio;

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

        System.Console.WriteLine("🦄 STT Orchestrator CONSTRUCTOR - Instance Created");
        _logger.LogInformation("🔧 STT Orchestrator initialized with direct singleton injection");
    }

    /// <summary>
    /// Process audio stream with automatic language detection using candidate languages
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {      
        var googleSucceeded = false;
        var fallbackMessage = "[Google Auto-Detection Error Fallback] Processing failed";
        var results = new List<TranscriptionResult>();

        // Priority 1: Google Auto-Detection with candidate languages (handles WebM/Opus chunks with language detection)
        IAsyncEnumerable<TranscriptionResult>? googleResults = null;
        try
        {
            _logger.LogDebug("🔍 STT Orchestrator: Attempting Google Auto-Detection");
            // Use proper auto language detection method with candidate languages
            googleResults = _googleSTT.ProcessAutoLanguageDetectionAsync(audioStream, candidateLanguages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT Orchestrator: Google Auto-Detection initialization failed");
        }

        if (googleResults != null)
        {
            try
            {
                await foreach (var result in googleResults)
                {
                    if (result.IsFinal)
                    {
                        googleSucceeded = true;
                    }
                    
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ STT Orchestrator: Google Auto-Detection streaming failed");
            }
        }

        // Return collected results if Google succeeded
        if (googleSucceeded)
        {
            foreach (var result in results)
            {
                yield return result;
            }
        }
        else
        {
            // If Google Auto-Detection failed, provide fallback result to avoid blocking the pipeline
            _logger.LogWarning("⚠️ STT Orchestrator: Google Auto-Detection failed, sending fallback message");
            
            var primaryLanguage = candidateLanguages.FirstOrDefault() ?? "en-US";
            yield return new TranscriptionResult
            {
                Text = fallbackMessage,
                Language = primaryLanguage,
                Confidence = 0.1f,
                IsFinal = true,
                Timestamp = TimeSpan.Zero // Use TimeSpan.Zero instead of DateTime
            };
        }
    }
}