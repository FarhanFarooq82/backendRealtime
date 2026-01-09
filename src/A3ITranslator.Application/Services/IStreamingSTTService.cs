using System.Threading.Channels;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface IStreamingSTTService
{
    /// <summary>
    /// Process audio stream with automatic language detection using candidate languages
    /// </summary>
    IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        CancellationToken cancellationToken = default);
}