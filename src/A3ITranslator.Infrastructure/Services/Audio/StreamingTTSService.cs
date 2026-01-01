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

    public StreamingTTSService(IOptions<ServiceOptions> options, ILogger<StreamingTTSService> logger)
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

        using var synthesizer = new SpeechSynthesizer(config, null);

        // Standard synthesis for now; could be optimized for true streaming if input was a stream, 
        // but input is a text sentence.
        using var result = await synthesizer.StartSpeakingTextAsync(text);
        using var audioStream = AudioDataStream.FromResult(result);

        var buffer = new byte[16000];
        uint bytesRead;

        while ((bytesRead = audioStream.ReadData(buffer)) > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            
            yield return new TTSChunk { AudioData = chunk };
        }
    }
}
