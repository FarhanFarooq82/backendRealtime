using MediatR;
using A3ITranslator.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;

public class ProcessAudioChunkHandler : IRequestHandler<ProcessAudioChunkCommand, bool>
{
    private readonly IConversationOrchestrator _conversationOrchestrator;
    private readonly ILogger<ProcessAudioChunkHandler> _logger;
    
    public ProcessAudioChunkHandler(
        IConversationOrchestrator conversationOrchestrator,
        ILogger<ProcessAudioChunkHandler> logger)
    {
        _conversationOrchestrator = conversationOrchestrator;
        _logger = logger;
    }

    public async Task<bool> Handle(ProcessAudioChunkCommand request, CancellationToken cancellationToken)
    {        
        _logger.LogDebug("Processing audio chunk for connection {ConnectionId}", request.ConnectionId);

        // Delegate to ConversationOrchestrator - it handles session management internally
        await _conversationOrchestrator.ProcessAudioChunkAsync(request.ConnectionId, request.AudioData);

        return true;
    }
}
