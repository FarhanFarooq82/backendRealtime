
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface IConnectionManager
{
    Task<ConnectionInitResult> InitializeConnectionAsync(
        string connectionId, 
        string? sessionId = null,
        string? primaryLang = null,
        string? secondaryLang = null);
    
    Task CleanupConnectionAsync(string connectionId);
    Task<UserAudioState?> GetSessionAsync(string connectionId);
}
