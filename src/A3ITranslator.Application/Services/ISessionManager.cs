using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

public interface ISessionManager
{
    ConversationSession? GetSession(string connectionId);
    ConversationSession GetOrCreateSession(string connectionId); // ✅ Add this method
    Task<ConversationSession> CreateSessionAsync(
        string connectionId, 
        string? sessionId = null, 
        string? primaryLang = null, 
        string? secondaryLang = null);
    Task AddConversationTurnAsync(string connectionId, ConversationTurn turn);
    Task EndSessionAsync(string connectionId);
    Task<IEnumerable<ConversationSession>> GetActiveSessionsAsync();
}