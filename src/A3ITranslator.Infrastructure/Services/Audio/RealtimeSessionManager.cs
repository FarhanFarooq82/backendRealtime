using A3ITranslator.Application.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

/// <summary>
/// Legacy session manager - use SessionManager instead
/// </summary>
public class RealtimeSessionManager
{
    private readonly ConcurrentDictionary<string, UserAudioState> _sessions = new();
    private readonly ILogger<RealtimeSessionManager> _logger;

    public RealtimeSessionManager(ILogger<RealtimeSessionManager> logger)
    {
        _logger = logger;
    }

    public UserAudioState? GetSession(string connectionId)
    {
        return _sessions.GetValueOrDefault(connectionId);
    }

    public UserAudioState GetOrCreateSession(string connectionId)
    {
        return _sessions.GetOrAdd(connectionId, id => new UserAudioState
        {
            ConnectionId = id
        });
    }

    public void RemoveSession(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            session?.Dispose(); // Now works because UserAudioState implements IDisposable
            _logger.LogInformation("Removed session: {ConnectionId}", connectionId);
        }
    }

    public void CleanupSession(UserAudioState session)
    {
        session.AudioStreamChannel.Writer.Complete();
        session.FinalTranscript = string.Empty;
    }
}