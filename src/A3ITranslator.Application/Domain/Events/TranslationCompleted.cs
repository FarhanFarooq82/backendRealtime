using MediatR;
using A3ITranslator.Application.Domain.Entities;

namespace A3ITranslator.Application.Domain.Events;

public record TranslationCompleted(
    ConversationSession Session,
    ConversationTurn Turn,
    string TranslatedText,
    string TargetLanguage
) : INotification;
