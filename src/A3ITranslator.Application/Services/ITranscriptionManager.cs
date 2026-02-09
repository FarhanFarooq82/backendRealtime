using A3ITranslator.Application.Models;
using System.Threading.Channels;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Result of a transcription competition between multiple languages
/// </summary>
public class TranscriptionCompetitionResult
{
    public string WinnerLanguage { get; set; } = string.Empty;
    public List<TranscriptionResult> WinnerResults { get; set; } = new();
    public string WinnerBestText { get; set; } = string.Empty;
    public float WinnerConfidence { get; set; }
    
    public string LoserLanguage { get; set; } = string.Empty;
    public List<TranscriptionResult> LoserResults { get; set; } = new();
    public string LoserBestText { get; set; } = string.Empty;
    public float LoserConfidence { get; set; }
    
    public double TotalDurationSeconds { get; set; }

    // Backward compatibility for now (mapping to Winner)
    public List<TranscriptionResult> AllResults => WinnerResults;
    public string BestText => WinnerBestText;
    public float Confidence => WinnerConfidence;
}

/// <summary>
/// Manages transcription competition and winner selection between multiple languages
/// </summary>
public interface ITranscriptionManager
{
    /// <summary>
    /// Run a transcription competition between primary and secondary languages
    /// </summary>
    Task<TranscriptionCompetitionResult> RunCompetitionAsync(
        ChannelReader<byte[]> primaryAudio,
        ChannelReader<byte[]> secondaryAudio,
        string connectionId,
        string primaryLanguage,
        string secondaryLanguage,
        Func<bool> isUtteranceCompleted,
        Func<string, TranscriptionResult, Task> onPartialResult,
        CancellationToken cancellationToken);
}
