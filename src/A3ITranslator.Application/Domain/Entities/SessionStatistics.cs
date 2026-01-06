namespace A3ITranslator.Application.Domain.Entities;

/// <summary>
/// Comprehensive session statistics and metrics
/// </summary>
public class SessionStatistics
{
    // Basic Metrics
    public int TotalTurns { get; set; }
    public int TotalSpeechTurns => SpeechTurns;
    public int TotalTranslations => TranslationTurns;
    public TimeSpan TotalDuration => DateTime.UtcNow - SessionStart;
    public DateTime SessionStart { get; init; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    
    // Audio Metrics
    public long TotalAudioBytes { get; set; }
    public TimeSpan TotalSpeechDuration { get; set; }
    public int AudioChunksProcessed { get; set; }
    
    // Language Detection
    public Dictionary<string, int> LanguageDetectionAttempts { get; } = new();
    public Dictionary<string, int> LanguageConfidenceScores { get; } = new();
    
    // Speaker Metrics
    public Dictionary<string, SpeakerStats> SpeakerStatistics { get; } = new();
    public int UniqueSpeakers => SpeakerStatistics.Count;
    
    // Turn Counters
    public int SpeechTurns { get; set; }
    public int TranslationTurns { get; set; }
    public int ErrorTurns { get; set; }
    
    // Quality Metrics
    public float AverageConfidence { get; set; }
    public float SuccessRate { get; set; }
    
    public void UpdateActivity()
    {
        LastActivity = DateTime.UtcNow;
    }
    
    public void RecordSpeechTurn(string speakerId, TimeSpan duration, float confidence)
    {
        SpeechTurns++;
        TotalSpeechDuration = TotalSpeechDuration.Add(duration);
        
        if (!SpeakerStatistics.ContainsKey(speakerId))
        {
            SpeakerStatistics[speakerId] = new SpeakerStats { SpeakerId = speakerId };
        }
        
        SpeakerStatistics[speakerId].TurnCount++;
        SpeakerStatistics[speakerId].TotalDuration = SpeakerStatistics[speakerId].TotalDuration.Add(duration);
        SpeakerStatistics[speakerId].UpdateConfidence(confidence);
        
        UpdateAverageConfidence();
    }
    
    public void RecordAudioChunk(int bytes)
    {
        TotalAudioBytes += bytes;
        AudioChunksProcessed++;
    }
    
    private void UpdateAverageConfidence()
    {
        if (SpeakerStatistics.Any())
        {
            AverageConfidence = SpeakerStatistics.Values.Average(s => s.AverageConfidence);
        }
    }
}

public class SpeakerStats
{
    public string SpeakerId { get; init; } = string.Empty;
    public int TurnCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public float AverageConfidence { get; set; }
    public DateTime FirstSeen { get; init; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public List<float> ConfidenceScores { get; } = new();
    
    public void UpdateConfidence(float confidence)
    {
        ConfidenceScores.Add(confidence);
        AverageConfidence = ConfidenceScores.Average();
        LastSeen = DateTime.UtcNow;
    }
}
