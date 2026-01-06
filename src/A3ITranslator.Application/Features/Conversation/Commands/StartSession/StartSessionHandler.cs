using MediatR;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Features.Conversation.Commands.StartSession;

public class StartSessionHandler : IRequestHandler<StartSessionCommand, bool>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<StartSessionHandler> _logger;

    public StartSessionHandler(
        ISessionRepository sessionRepository, 
        ILogger<StartSessionHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<bool> Handle(StartSessionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting new domain session {SessionId} for connection {ConnectionId}", request.SessionId, request.ConnectionId);

            var session = ConversationSession.Create(request.ConnectionId, request.SessionId);
            session.PrimaryLanguage = request.PrimaryLanguage;
            session.SecondaryLanguage = request.SecondaryLanguage;
            session.IsLanguageConfirmed = false; // Default logic

            await _sessionRepository.SaveAsync(session, cancellationToken);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start domain session {SessionId}", request.SessionId);
            return false;
        }
    }
}
