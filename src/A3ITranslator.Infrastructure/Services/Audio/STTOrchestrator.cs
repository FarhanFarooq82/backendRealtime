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

        // Priority 1: Google Auto-Detection with candidate languages (handles WebM/Opus chunks with language detection)
        IAsyncEnumerable<TranscriptionResult>? googleResults = null;
        try
        {
            _logger.LogDebug("🔍 STT Orchestrator: Attempting Google Auto-Detection");
            // Use proper auto language detection method with candidate languages
            googleResults = _googleSTT.ProcessAutoLanguageDetectionAsync(audioStream, candidateLanguages, cancellationToken);
            Console.WriteLine($"🔍 TIMESTAMP_STT_Orchestrator: {DateTime.Now}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT Orchestrator: Google Auto-Detection initialization failed");
        }

        if (googleResults != null)
        {
            await foreach (var result in googleResults)
            {
                Console.WriteLine($"TIMESTAMP_STT_ORCHESTRATOR_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - Text: '{result.Text}' - IsFinal: {result.IsFinal}");
                
                if (result.IsFinal)
                {
                    googleSucceeded = true;
                }
                
                // Yield immediately instead of buffering
                yield return result;
            }
        }

        // Only provide fallback if Google completely failed and we haven't yielded any results
        if (!googleSucceeded)
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