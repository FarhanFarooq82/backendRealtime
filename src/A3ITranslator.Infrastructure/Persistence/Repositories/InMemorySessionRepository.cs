using System.Collections.Concurrent;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Persistence.Repositories;

public class InMemorySessionRepository : ISessionRepository
{
    // Thread-safe storage mimicking a database
    private static readonly ConcurrentDictionary<string, ConversationSession> _sessionsById = new();
    private static readonly ConcurrentDictionary<string, string> _connectionToSessionMap = new();
    private readonly ILogger<InMemorySessionRepository> _logger;

    public InMemorySessionRepository(ILogger<InMemorySessionRepository> logger)
    {
        _logger = logger;
    }

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
        
        _logger.LogDebug("Session {SessionId} saved for connection {ConnectionId}", session.SessionId, session.ConnectionId);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove session by connection ID (used for cleanup on disconnect)
    /// </summary>
    public Task RemoveByConnectionIdAsync(string connectionId, CancellationToken ct = default)
    {
        if (_connectionToSessionMap.TryRemove(connectionId, out var sessionId))
        {
            if (_sessionsById.TryRemove(sessionId, out var session))
            {
                // Clean up session resources
                session.EndSession(SessionStatus.Terminated);
                
                _logger.LogInformation("🧹 Cleaned up session {SessionId} for disconnected connection {ConnectionId}", 
                    sessionId, connectionId);
                
                return Task.CompletedTask;
            }
        }

        _logger.LogWarning("No session found for connection {ConnectionId} to remove", connectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get all active sessions (useful for monitoring and cleanup)
    /// </summary>
    public Task<IEnumerable<ConversationSession>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        var activeSessions = _sessionsById.Values
            .Where(s => s.Status == SessionStatus.Active)
            .ToList();

        return Task.FromResult<IEnumerable<ConversationSession>>(activeSessions);
    }
}
