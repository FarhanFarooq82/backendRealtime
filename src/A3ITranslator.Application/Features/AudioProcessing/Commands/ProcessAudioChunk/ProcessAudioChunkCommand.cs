using MediatR;

namespace A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;

public record ProcessAudioChunkCommand(
    string ConnectionId, 
    byte[] AudioData, 
    long? Timestamp = null
) : IRequest<bool>;
