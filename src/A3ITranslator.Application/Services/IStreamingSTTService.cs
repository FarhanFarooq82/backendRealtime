using System.Threading.Channels;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface IStreamingSTTService
{
    IAsyncEnumerable<TranscriptionResult> TranscribeStreamAsync(
        ChannelReader<byte[]> audioStream, 
        string language, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Process audio stream with automatic language detection
    /// Renamed from TranscribeStreamWithAutoDetectionAsync for clarity
    /// </summary>
    IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Transcribe audio stream with automatic language detection
    /// [Obsolete] Use ProcessAutoLanguageDetectionAsync instead
    /// </summary>
    [Obsolete("Use ProcessAutoLanguageDetectionAsync instead. This method will be removed in a future version.", false)]
    IAsyncEnumerable<TranscriptionResult> TranscribeStreamWithAutoDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        CancellationToken cancellationToken = default);
        
    Task<TranscriptionResult> TranscribeAudioAsync(
        byte[] audioData, 
        string language, 
        CancellationToken cancellationToken = default);
}