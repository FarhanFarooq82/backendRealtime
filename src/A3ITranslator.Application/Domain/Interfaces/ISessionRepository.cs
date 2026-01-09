using A3ITranslator.Application.Domain.Entities;

namespace A3ITranslator.Application.Domain.Interfaces;

public interface ISessionRepository
{
    Task<ConversationSession?> GetByIdAsync(string sessionId, CancellationToken ct = default);
    Task<ConversationSession?> GetByConnectionIdAsync(string connectionId, CancellationToken ct = default);
    Task SaveAsync(ConversationSession session, CancellationToken ct = default);
    Task RemoveByConnectionIdAsync(string connectionId, CancellationToken ct = default);
    Task<IEnumerable<ConversationSession>> GetActiveSessionsAsync(CancellationToken ct = default);
}
