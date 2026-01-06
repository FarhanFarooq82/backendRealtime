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
    /// Process audio stream with automatic language detection
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌍 STT Orchestrator: Starting Google Auto-Detection with candidates: [{Languages}]", 
            string.Join(", ", candidateLanguages));
        Console.WriteLine($"🌍 STT ORCHESTRATOR: Auto-Detection starting with {candidateLanguages.Length} candidate languages");
        
        var googleSucceeded = false;

        // Priority 1: Google Auto-Detection (handles WebM/Opus chunks with language detection)
        IAsyncEnumerable<TranscriptionResult>? googleResults = null;
        try
        {
            _logger.LogInformation("🎵 STT Orchestrator: Starting Google WebM streaming for {Language}", "en-US");
            googleResults = _googleSTT.TranscribeWebMStreamAsync(audioStream, "en-US", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STT Orchestrator: Google Auto-Detection initialization failed");
        }

        if (googleResults != null)
        {
            await foreach (var result in googleResults)
            {
                if (result.IsFinal)
                {
                    googleSucceeded = true;
                }

                // 🎤 ORCHESTRATOR: Show Google Auto-Detection Results Flow
                Console.WriteLine($"");
                Console.WriteLine($"🔄🌍 STT ORCHESTRATOR AUTO-DETECTION RESULT FLOW 🌍");
                Console.WriteLine($"📍 Source: Google Auto-Detection STT");
                Console.WriteLine($"📝 Text: \"{result.Text}\"");
                Console.WriteLine($"🔒 Is Final: {result.IsFinal}");
                Console.WriteLine($"📊 Confidence: {result.Confidence:P1}");
                Console.WriteLine($"🎯 Detected Language: {result.Language}");
                Console.WriteLine($"📡 → Sending to STT Processor...");
                Console.WriteLine($"=======================================");
                Console.WriteLine($"");
                
                yield return result;
            }
        }

        // If Google Auto-Detection failed, log the failure
        if (!googleSucceeded)
        {
            _logger.LogWarning("⚠️ STT Orchestrator: Google Auto-Detection processing failed - no results produced");
            Console.WriteLine("⚠️ STT Orchestrator: Google Auto-Detection processing failed - no results produced");
        }
    }

    /// <summary>
    /// [Obsolete] Use ProcessAutoLanguageDetectionAsync instead
    /// Transcribe audio stream with Google's automatic language detection
    /// </summary>
    [Obsolete("Use ProcessAutoLanguageDetectionAsync instead. This method will be removed in a future version.", false)]
    public async IAsyncEnumerable<TranscriptionResult> TranscribeStreamWithAutoDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in ProcessAutoLanguageDetectionAsync(audioStream, candidateLanguages, cancellationToken))
        {
            yield return result;
        }
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
                if (result.IsFinal)
                {
                    googleSucceeded = true;
                }

                // 🎤 ORCHESTRATOR: Show Google STT Results Flow
                Console.WriteLine($"");
                Console.WriteLine($"🔄📡 STT ORCHESTRATOR RESULT FLOW 📡🔄");
                Console.WriteLine($"📍 Source: Google WebM STT");
                Console.WriteLine($"📝 Text: \"{result.Text}\"");
                Console.WriteLine($"🔒 Is Final: {result.IsFinal}");
                Console.WriteLine($"📊 Confidence: {result.Confidence:P1}");
                Console.WriteLine($"🎯 Language: {result.Language}");
                Console.WriteLine($"📡 → Sending to STT Processor...");
                Console.WriteLine($"=======================================");
                Console.WriteLine($"");
                
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