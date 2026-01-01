using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;


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
    /// Primary streaming method - uses Google WebM for WebM/Opus chunks from frontend
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> TranscribeStreamAsync(
        ChannelReader<byte[]> audioStream, 
        string language, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("� STT Orchestrator starting for {Language} - using Google WebM (primary)", language);
        Console.WriteLine($"� CONSOLE: STT Orchestrator starting for {language} - using Google WebM (primary)");

        var googleSucceeded = false;

        // Priority 1: Google WebM (handles WebM/Opus chunks directly from frontend)
        IAsyncEnumerable<TranscriptionResult>? googleResults = null;
        try
        {
            _logger.LogInformation("🎵 STT Orchestrator: Starting Google WebM streaming for {Language}", language);
            googleResults = _googleSTT.TranscribeWebMStreamAsync(audioStream, language, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT Orchestrator: Google WebM initialization failed");
        }

        if (googleResults != null)
        {
            await foreach (var result in googleResults)
            {
                googleSucceeded = true;
                Console.WriteLine($"🎵 STT Orchestrator: Yielding Google WebM result: \"{result.Text}\" (IsFinal: {result.IsFinal})");
                yield return result;
            }
        }

        // If Google WebM failed, log the failure (Azure fallback would require PCM conversion)
        if (!googleSucceeded)
        {
            _logger.LogWarning("⚠️ STT Orchestrator: Google WebM processing failed - no results produced");
            Console.WriteLine("⚠️ STT Orchestrator: Google WebM processing failed - no results produced");
        }
    }

    public async Task<TranscriptionResult> TranscribeAudioAsync(
        byte[] audioData, 
        string language, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // ✅ Try Azure first (as requested)
            _logger.LogInformation("🎯 Trying Azure STT for single audio transcription");
            return await _azureSTT.TranscribeAudioAsync(audioData, language, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Azure STT failed, trying Google fallback");
            
            try
            {
                // Fallback to Google
                return await _googleSTT.TranscribeAudioAsync(audioData, language, cancellationToken);
            }
            catch (Exception googleEx)
            {
                _logger.LogError(googleEx, "❌ Both Azure and Google STT services failed");
                
                return new TranscriptionResult
                {
                    Text = "",
                    Language = language,
                    Confidence = 0.0,
                    IsFinal = true
                };
            }
        }
    }
}