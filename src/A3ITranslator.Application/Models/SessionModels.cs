using System.Threading.Channels;

namespace A3ITranslator.Application.Models;

/// <summary>
/// Complete conversation session with all tracking data
/// </summary>
public class ConversationSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public string ConnectionId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    
    // Language Settings
    public string PrimaryLanguage { get; set; } = "en";
    public string? SecondaryLanguage { get; set; }
    public bool IsLanguageConfirmed { get; set; }
    
    // Audio Streaming
    public Channel<byte[]> AudioStreamChannel { get; } = Channel.CreateUnbounded<byte[]>();
    public List<byte> AudioBuffer { get; } = new();
    
    // Speaker Management
    public SpeakerRegistry Speakers { get; } = new();
    public string? CurrentSpeakerId { get; set; }
    
    // Conversation History
    public List<ConversationTurn> ConversationHistory { get; } = new();
    public string FinalTranscript { get; set; } = string.Empty;
    
    // Session Statistics
    public SessionStatistics Statistics { get; } = new();
    
    // Session State
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public Dictionary<string, object> Metadata { get; } = new();
    
    // STT Processing State
    public bool SttProcessorRunning { get; set; } = false;

    public void UpdateActivity()
    {
        LastActivity = DateTime.UtcNow;
        Statistics.UpdateActivity();
    }

    public void AddConversationTurn(ConversationTurn turn)
    {
        ConversationHistory.Add(turn);
        Statistics.TotalTurns++;
        UpdateActivity();
    }

    public string GetEffectiveLanguage()
    {
        if (IsLanguageConfirmed) return PrimaryLanguage;
        
        // Get language from current speaker if available
        if (CurrentSpeakerId != null)
        {
            var speaker = Speakers.GetSpeaker(CurrentSpeakerId);
            if (!string.IsNullOrEmpty(speaker?.KnownLanguage))
                return speaker.KnownLanguage;
        }
        
        return PrimaryLanguage;
    }
}

public enum SessionStatus
{
    Active,
    Paused,
    Completed,
    Terminated,
    Error
}

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
    
    public void RecordLanguageDetection(string language, float confidence)
    {
        LanguageDetectionAttempts[language] = LanguageDetectionAttempts.GetValueOrDefault(language) + 1;
        LanguageConfidenceScores[language] = (int)(confidence * 100);
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