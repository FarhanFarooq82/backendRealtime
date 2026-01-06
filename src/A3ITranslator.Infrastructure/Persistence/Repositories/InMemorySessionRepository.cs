using System.Collections.Concurrent;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Domain.Interfaces;

namespace A3ITranslator.Infrastructure.Persistence.Repositories;

public class InMemorySessionRepository : ISessionRepository
{
    // Thread-safe storage mimicking a database
    private static readonly ConcurrentDictionary<string, ConversationSession> _sessionsById = new();
    private static readonly ConcurrentDictionary<string, string> _connectionToSessionMap = new();

    public Task<ConversationSession?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessionsById.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<ConversationSession?>(session);
        }
        return Task.FromResult<ConversationSession?>(null);
    }

    public Task<ConversationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken ct = default)
    {
        if (_connectionToSessionMap.TryGetValue(connectionId, out var sessionId))
        {
            return GetByIdAsync(sessionId, ct);
        }
        return Task.FromResult<ConversationSession?>(null);
    }

    public Task SaveAsync(ConversationSession session, CancellationToken ct = default)
    {
        // Update Session Index
        _sessionsById.AddOrUpdate(session.SessionId, session, (key, oldValue) => session);
        
        // Update Connection Index
        _connectionToSessionMap.AddOrUpdate(session.ConnectionId, session.SessionId, (key, oldValue) => session.SessionId);
        
        return Task.CompletedTask;
    }
}
