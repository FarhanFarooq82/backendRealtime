// In A3ITranslator.Infrastructure/Services/SessionManager.cs
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services;

public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public ConversationSession? GetSession(string connectionId)
    {
        return _sessions.GetValueOrDefault(connectionId);
    }

    // ✅ Add missing method
    public ConversationSession GetOrCreateSession(string connectionId)
    {
        return _sessions.GetOrAdd(connectionId, id => new ConversationSession
        {
            SessionId = id,
            ConnectionId = id,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        });
    }

    public async Task<ConversationSession> CreateSessionAsync(
        string connectionId, 
        string? sessionId = null, 
        string? primaryLang = null, 
        string? secondaryLang = null)
    {
        var session = new ConversationSession
        {
            SessionId = sessionId ?? connectionId,
            ConnectionId = connectionId,
            PrimaryLanguage = primaryLang ?? "en-US",
            SecondaryLanguage = secondaryLang,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        _sessions[connectionId] = session;
        _logger.LogInformation("Created session {SessionId} for connection {ConnectionId}", 
            session.SessionId, connectionId);
        
        return session;
    }

    public async Task AddConversationTurnAsync(string connectionId, ConversationTurn turn)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            session.ConversationHistory.Add(turn);
            session.LastActivity = DateTime.UtcNow;
            _logger.LogDebug("Added turn to session {SessionId}: {Text}", connectionId, turn.OriginalText);
        }
    }

    public async Task EndSessionAsync(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            session.AudioStreamChannel.Writer.Complete();
            _logger.LogInformation("Ended session: {SessionId}", connectionId);
        }
    }

    public async Task<IEnumerable<ConversationSession>> GetActiveSessionsAsync()
    {
        return _sessions.Values.ToList();
    }
}