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

    public AzureStreamingTTSService(IOptions<ServiceOptions> options, ILogger<AzureStreamingTTSService> logger)
    {
        _options = options.Value;
        _logger = logger;
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

        // Use PullStream to get bytes directly
        using var synthesizer = new SpeechSynthesizer(config, null); // null output means memory

        // We use SynthesizeSpeechToStreamAsync for non-streaming text input (since we send sentence by sentence)
        // Optimization: For long text we could use SpeakTextAsync with events, but for sentence-level, standard synthesis is fine.
        // However, to be "Streaming" to the client, we want to yield bytes as Azure generates them.
        
        using var result = await synthesizer.StartSpeakingTextAsync(text);
        
        // Read the stream from Azure
        using var audioStream = AudioDataStream.FromResult(result);
        
        var buffer = new byte[16000]; // 16kb buffer
        uint bytesRead;
        
        while ((bytesRead = audioStream.ReadData(buffer)) > 0)
        {
            // Create a copy of the chunk to yield
            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            
            yield return new TTSChunk { AudioData = chunk };
        }
    }
}
