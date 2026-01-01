using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class ConnectionManager : IConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, UserAudioState> _sessions = new();
    
    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    public Task<ConnectionInitResult> InitializeConnectionAsync(
        string connectionId,
        string? sessionId = null,
        string? primaryLang = null,
        string? secondaryLang = null)
    {
        var result = new ConnectionInitResult
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString(),
            PrimaryLanguage = primaryLang ?? "en-US",
            SecondaryLanguage = secondaryLang ?? "en-US",
            Success = true
        };

        var session = new UserAudioState
        {
            ConnectionId = connectionId,
            KnownSpeakerLanguage = result.PrimaryLanguage,
            FinalTranscript = "",
            AudioStreamChannel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(),
            IsLanguageConfirmed = false
        };

        _sessions.TryAdd(connectionId, session);
        
        _logger.LogInformation("Initialized connection {ConnectionId} with session {SessionId}", 
            connectionId, result.SessionId);

        return Task.FromResult(result);
    }

    public Task CleanupConnectionAsync(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            session.Dispose();
            _logger.LogInformation("Cleaned up connection {ConnectionId}", connectionId);
        }
        return Task.CompletedTask;
    }

    public Task<UserAudioState?> GetSessionAsync(string connectionId)
    {
        _sessions.TryGetValue(connectionId, out var session);
        return Task.FromResult(session);
    }
}
