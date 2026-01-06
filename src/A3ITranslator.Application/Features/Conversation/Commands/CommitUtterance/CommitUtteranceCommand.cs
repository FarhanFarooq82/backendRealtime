using MediatR;

namespace A3ITranslator.Application.Features.Conversation.Commands.CommitUtterance;

public record CommitUtteranceCommand(string ConnectionId) : IRequest<CommitUtteranceResult>;

public record CommitUtteranceResult(
    string RequestId,
    bool Success,
    string? Transcript,
    string? ErrorMessage = null
);
