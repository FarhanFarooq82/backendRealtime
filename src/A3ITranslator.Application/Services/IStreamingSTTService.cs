using System.Threading.Channels;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface IStreamingSTTService
{
    IAsyncEnumerable<TranscriptionResult> TranscribeStreamAsync(
        ChannelReader<byte[]> audioStream, 
        string language, 
        CancellationToken cancellationToken = default);
        
    Task<TranscriptionResult> TranscribeAudioAsync(
        byte[] audioData, 
        string language, 
        CancellationToken cancellationToken = default);
}