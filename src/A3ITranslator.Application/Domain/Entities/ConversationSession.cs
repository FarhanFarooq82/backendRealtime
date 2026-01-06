using System.Threading.Channels;
using MediatR;
using A3ITranslator.Application.Domain.ValueObjects;
using A3ITranslator.Application.Domain.Enums;
using A3ITranslator.Application.Domain.Events;
using System.Linq;

namespace A3ITranslator.Application.Domain.Entities;

public class ConversationSession
{
    private readonly object _lock = new();

    public string SessionId { get; private set; }
    public string ConnectionId { get; private set; }
    public DateTime StartTime { get; private set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    // Language Settings
    public string PrimaryLanguage { get; set; } = "en";
    public string? SecondaryLanguage { get; set; }
    public bool IsLanguageConfirmed { get; set; }

    // Audio Streaming (Transient)
    public Channel<byte[]> AudioStreamChannel { get; } = Channel.CreateUnbounded<byte[]>();
    public List<byte> AudioBuffer { get; } = new();

    // Speaker Management
    private readonly List<Speaker> _speakers = new();
    public IReadOnlyCollection<Speaker> Speakers => _speakers.AsReadOnly();
    public string? CurrentSpeakerId { get; set; }

    // Conversation History
    // Conversation History (Thread-Safe)
    private readonly List<ConversationTurn> _conversationHistory = new();
    public IReadOnlyList<ConversationTurn> ConversationHistory 
    {
        get { lock(_lock) return _conversationHistory.ToList(); }
    }
    
    public string FinalTranscript { get; set; } = string.Empty;

    public void AppendTranscript(string text)
    {
        lock(_lock)
        {
            FinalTranscript += text;
        }
    }

    // Session Statistics
    public SessionStatistics Statistics { get; } = new();

    // Session State
    public SessionStatus Status { get; private set; } = SessionStatus.Active;
    public Dictionary<string, object> Metadata { get; } = new();

    // STT Processing State (Transient)
    public bool SttProcessorRunning { get; set; } = false;

    private ConversationSession() { }

    public static ConversationSession Create(string connectionId, string sessionId = null)
    {
        return new ConversationSession
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString(),
            ConnectionId = connectionId,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };
    }

    public void UpdateActivity()
    {
        LastActivity = DateTime.UtcNow;
        Statistics.UpdateActivity();
    }

    public void AddConversationTurn(ConversationTurn turn)
    {
        lock(_lock)
        {
            _conversationHistory.Add(turn);
            Statistics.TotalTurns++;
            UpdateActivity();
        }
    }

    public string GetEffectiveLanguage()
    {
        if (IsLanguageConfirmed) return PrimaryLanguage;

        // Get language from current speaker if available
        if (CurrentSpeakerId != null)
        {
            var speaker = GetSpeaker(CurrentSpeakerId);
            if (!string.IsNullOrEmpty(speaker?.Language))
                return speaker.Language;
        }

        return PrimaryLanguage;
    }

    // --- Domain Events ---
    private readonly List<INotification> _domainEvents = new();
    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void AddDomainEvent(INotification eventItem) => _domainEvents.Add(eventItem);

    // --- Business Logic ---

    public ConversationTurn CommitCurrentTranscript()
    {
        lock(_lock)
        {
            if (string.IsNullOrWhiteSpace(FinalTranscript))
            {
                // Logic for empty transcript...
            }
    
            string speakerName = "Unknown";
            if (CurrentSpeakerId != null)
            {
                var speaker = GetSpeaker(CurrentSpeakerId);
                if (speaker != null) speakerName = speaker.DisplayName;
            }
    
            var turn = ConversationTurn.CreateSpeech(
                CurrentSpeakerId ?? "unknown",
                speakerName,
                FinalTranscript,
                GetEffectiveLanguage()
            );
    
            AddConversationTurn(turn);
            
            // Capture transcript before clearing
            string committedTranscript = FinalTranscript;
            
            // Clear transcript buffer
            FinalTranscript = string.Empty;
            
            // Clear Audio Buffer (Important for memory management)
            AudioBuffer.Clear();
    
            // Emit Event
            AddDomainEvent(new Events.UtteranceCommitted(this, turn, committedTranscript));
    
            return turn;
        }
    }

    // --- Speaker Management Logic (Migrated from SpeakerRegistry) ---

    public Speaker? GetSpeaker(string speakerId) => _speakers.FirstOrDefault(s => s.SpeakerId == speakerId);

    public void AddSpeaker(Speaker speaker)
    {
        if (!_speakers.Any(s => s.SpeakerId == speaker.SpeakerId))
        {
            _speakers.Add(speaker);
        }
    }

    public string? FindMatchingSpeaker(VoiceCharacteristics characteristics, float threshold = 0.8f)
    {
        return _speakers
            .Select(s => new { Speaker = s, Score = CalculateSimilarity(characteristics, s.VoiceCharacteristics) })
            .Where(x => x.Score >= threshold)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Speaker.SpeakerId;
    }

    private static float CalculateSimilarity(VoiceCharacteristics a, VoiceCharacteristics b)
    {
        if (a.Pitch == 0 && b.Pitch == 0) return 1.0f;
        if (a.Pitch == 0 || b.Pitch == 0) return 0.0f;
        return 1.0f - Math.Abs(a.Pitch - b.Pitch) / Math.Max(a.Pitch, b.Pitch);
    }

    // Session Lifecycle Methods
    public void EndSession(SessionStatus endStatus = SessionStatus.Completed)
    {
        Status = endStatus;
        AudioStreamChannel.Writer.Complete();
        UpdateActivity(); // Update last activity timestamp
    }

    public void TerminateSession()
    {
        EndSession(SessionStatus.Terminated);
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
