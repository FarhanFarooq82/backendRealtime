using System.Threading.Channels;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface IStreamingSTTService
{
    /// <summary>
    /// Process audio stream with the specified language for transcription
    /// </summary>
    IAsyncEnumerable<TranscriptionResult> ProcessStreamAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        CancellationToken cancellationToken = default);
}