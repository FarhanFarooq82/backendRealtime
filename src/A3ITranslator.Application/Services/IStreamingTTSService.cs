namespace A3ITranslator.Application.Services;

public class TTSChunk
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string BoundaryType { get; set; } = "sentence"; // sentence, punctuation, end, chunk
    public string AssociatedText { get; set; } = string.Empty;
    public bool IsFirstChunk { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
}

public interface IStreamingTTSService
{
    /// <summary>
    /// Converts text to speech stream.
    /// Can be called multiple times for sequential sentences.
    /// </summary>
    IAsyncEnumerable<TTSChunk> SynthesizeStreamAsync(string text, string language, string voiceName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Synthesizes text to speech and sends to client.
    /// Automatically selects appropriate voice based on speaker profile.
    /// </summary>
    Task<string> SynthesizeAndNotifyAsync(
        string connectionId, 
        string text, 
        string language, 
        string? speakerId = null,
        string estimatedGender = "Unknown", 
        bool isPremium = true, 
        CancellationToken cancellationToken = default);
}
