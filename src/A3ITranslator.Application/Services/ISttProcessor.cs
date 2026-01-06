namespace A3ITranslator.Application.Services;

/// <summary>
/// Clean STT processor interface with Google auto-detection
/// </summary>
public interface ISttProcessor
{
    /// <summary>
    /// Start STT with Google's automatic language detection from candidate pool
    /// </summary>
    Task StartAutoLanguageDetectionAsync(string connectionId, string[] candidateLanguages, CancellationToken cancellationToken);
    
    /// <summary>
    /// Cleanup resources for a specific connection to prevent memory leaks
    /// </summary>
    void CleanupConnection(string connectionId);
}