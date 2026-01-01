namespace A3ITranslator.Application.Services;

public class TTSChunk
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string BoundaryType { get; set; } = "sentence"; // sentence, punctuation, end
}

public interface IStreamingTTSService
{
    /// <summary>
    /// Converts text to speech stream.
    /// Can be called multiple times for sequential sentences.
    /// </summary>
    IAsyncEnumerable<TTSChunk> SynthesizeStreamAsync(string text, string language, string voiceName, CancellationToken cancellationToken = default);
}
