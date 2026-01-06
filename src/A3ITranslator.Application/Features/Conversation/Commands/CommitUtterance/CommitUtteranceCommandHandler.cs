using MediatR;
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Features.Conversation.Commands.CommitUtterance;

public class CommitUtteranceCommandHandler : IRequestHandler<CommitUtteranceCommand, CommitUtteranceResult>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<CommitUtteranceCommandHandler> _logger;

    public CommitUtteranceCommandHandler(
        ISessionRepository sessionRepository,
        IPublisher publisher,
        ILogger<CommitUtteranceCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<CommitUtteranceResult> Handle(CommitUtteranceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _sessionRepository.GetByConnectionIdAsync(request.ConnectionId, cancellationToken);
            if (session == null)
            {
                return new CommitUtteranceResult(Guid.NewGuid().ToString(), false, null, "Session not found");
            }

            if (string.IsNullOrWhiteSpace(session.FinalTranscript))
            {
                // Nothing to commit
                return new CommitUtteranceResult(Guid.NewGuid().ToString(), true, null, "No transcript to commit");
            }

            // --- DOMAIN LOGIC ---
            var turn = session.CommitCurrentTranscript();
            // -------------------

            // Save state (Persistence)
            await _sessionRepository.SaveAsync(session, cancellationToken); // This saves the updated transcript (empty) and new history

            // Publish triggered events (Side effects: GenAI, TTS, etc.)
            foreach (var domainEvent in session.DomainEvents)
            {
                await _publisher.Publish(domainEvent, cancellationToken);
            }
            session.ClearDomainEvents();

            _logger.LogInformation("Committed utterance for Session {SessionId}. Turn ID: {TurnId}", session.SessionId, turn.TurnId);

            return new CommitUtteranceResult(
                Guid.NewGuid().ToString(),
                true,
                turn.OriginalText
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling CommitUtteranceCommand for Connection {ConnectionId}", request.ConnectionId);
            return new CommitUtteranceResult(Guid.NewGuid().ToString(), false, null, ex.Message);
        }
    }
}
