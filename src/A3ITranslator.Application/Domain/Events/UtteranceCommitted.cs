using MediatR;
using A3ITranslator.Application.Domain.Entities;

namespace A3ITranslator.Application.Domain.Events;

public record UtteranceCommitted(
    ConversationSession Session,
    ConversationTurn Turn,
    string Transcript
) : INotification;
