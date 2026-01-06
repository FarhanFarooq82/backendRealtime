using MediatR;

namespace A3ITranslator.Application.Features.Conversation.Commands.StartSession;

public record StartSessionCommand(
    string ConnectionId,
    string SessionId,
    string PrimaryLanguage = "en",
    string? SecondaryLanguage = null
) : IRequest<bool>;
