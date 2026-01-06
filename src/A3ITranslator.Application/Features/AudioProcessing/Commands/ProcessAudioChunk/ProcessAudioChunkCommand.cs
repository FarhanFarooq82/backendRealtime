using MediatR;

namespace A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;

public record ProcessAudioChunkCommand(string ConnectionId, string Base64AudioData) : IRequest<bool>;
