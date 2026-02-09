using MediatR;
using A3ITranslator.Application.Domain.Enums;
using A3ITranslator.Application.Domain.Events;
using A3ITranslator.Application.Models.Speaker;
using System.Linq;

namespace A3ITranslator.Application.Domain.Entities;

public class ConversationSession
{
    private readonly object _lock = new();

    public string SessionId { get; private set; } = string.Empty;
    public string ConnectionId { get; private set; } = string.Empty;
    public DateTime StartTime { get; private set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    // Language Settings
    public string PrimaryLanguage { get; set; } = "en";
    public string? SecondaryLanguage { get; set; }
    public bool IsLanguageConfirmed { get; set; }

    // Speaker Management
    private readonly List<SpeakerProfile> _speakers = new();
    public IReadOnlyCollection<SpeakerProfile> Speakers => _speakers.AsReadOnly();
    public string? CurrentSpeakerId { get; set; }

    // Fact Management
    private readonly Dictionary<string, Fact> _facts = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<Fact> Facts 
    {
        get { lock(_lock) return _facts.Values.ToList(); }
    }
    
    public void UpdateFact(Fact fact)
    {
        lock(_lock)
        {
            _facts[fact.Key] = fact;
            UpdateActivity();
        }
    }
    
    public void DeleteFact(string key)
    {
        lock(_lock)
        {
            if (_facts.Remove(key))
                UpdateActivity();
        }
    }

    // Conversation History
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

    public SessionStatistics Statistics { get; } = new();
    public SessionStatus Status { get; private set; } = SessionStatus.Active;
    public Dictionary<string, object> Metadata { get; } = new();

    private ConversationSession() { }

    public static ConversationSession Create(string connectionId, string? sessionId = null)
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
            turn.SequenceNumber = _conversationHistory.Count + 1;
            _conversationHistory.Add(turn);
            Statistics.TotalTurns++;
            UpdateActivity();
        }
    }

    public string GetEffectiveLanguage()
    {
        if (IsLanguageConfirmed) return PrimaryLanguage;

        if (CurrentSpeakerId != null)
        {
            var speaker = GetSpeaker(CurrentSpeakerId);
            var lang = speaker?.GetDominantLanguage();
            if (!string.IsNullOrEmpty(lang))
                return lang;
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
            string committedTranscript = FinalTranscript;
            FinalTranscript = string.Empty;
            
            AddDomainEvent(new Events.UtteranceCommitted(this, turn, committedTranscript));
    
            return turn;
        }
    }

    public SpeakerProfile? GetSpeaker(string speakerId) => _speakers.FirstOrDefault(s => s.SpeakerId == speakerId);

    public void AddSpeaker(SpeakerProfile speaker)
    {
        if (!_speakers.Any(s => s.SpeakerId == speaker.SpeakerId))
        {
            _speakers.Add(speaker);
        }
    }

    /// <summary>
    /// Retroactively merges two speakers in session history
    /// </summary>
    public void MergeSpeakers(string ghostId, string targetId)
    {
        lock (_lock)
        {
            var targetSpeaker = GetSpeaker(targetId);
            if (targetSpeaker == null) return;

            // 1. Update all conversation turns
            foreach (var turn in _conversationHistory.Where(t => t.SpeakerId == ghostId))
            {
                turn.UpdateSpeaker(targetId, targetSpeaker.DisplayName);
            }

            // 2. Remove the ghost from roster
            var ghost = _speakers.FirstOrDefault(s => s.SpeakerId == ghostId);
            if (ghost != null) _speakers.Remove(ghost);
        }
    }

    public void RemoveSpeaker(string speakerId)
    {
        lock (_lock)
        {
            var speaker = _speakers.FirstOrDefault(s => s.SpeakerId == speakerId);
            if (speaker != null) _speakers.Remove(speaker);
        }
    }

    public void EndSession(SessionStatus endStatus = SessionStatus.Completed)
    {
        Status = endStatus;
        UpdateActivity();
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
