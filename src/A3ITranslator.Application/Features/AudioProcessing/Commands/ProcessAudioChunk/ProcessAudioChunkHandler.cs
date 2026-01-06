using MediatR;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Services; // For ISttProcessor

namespace A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;

public class ProcessAudioChunkHandler : IRequestHandler<ProcessAudioChunkCommand, bool>
{
    private readonly ISessionRepository _sessionRepository; // ✅ PURE DOMAIN: Use Domain repository
    private readonly ISttProcessor _sttProcessor;
    
    public ProcessAudioChunkHandler(
        ISessionRepository sessionRepository, // ✅ PURE DOMAIN: Domain session repository
        ISttProcessor sttProcessor)
    {
        _sessionRepository = sessionRepository;
        _sttProcessor = sttProcessor;
    }

    public async Task<bool> Handle(ProcessAudioChunkCommand request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"🔥 HANDLER: ProcessAudioChunkCommand received for {request.ConnectionId}");
        
        // ✅ PURE DOMAIN: Use Domain repository to get session
        var session = await _sessionRepository.GetByConnectionIdAsync(request.ConnectionId, cancellationToken);
        if (session == null)
        {
            Console.WriteLine($"❌ HANDLER: Session not found for {request.ConnectionId}");
            return false;
        }
        
        Console.WriteLine($"✅ HANDLER: Session found for {request.ConnectionId}");

        // Convert base64 to bytes
        var audioBytes = Convert.FromBase64String(request.Base64AudioData);
        Console.WriteLine($"🎵 HANDLER: Converted {request.Base64AudioData.Length} base64 chars to {audioBytes.Length} bytes for {request.ConnectionId}");

        // 1. Add deep persistence (future feature)
        // session.AppendAudio(audioBytes); 
        
        // 2. Stream to STT Processor
        // Note: The original Orchestrator wrote to a Channel. 
        // Ideally, ISttProcessor should accept the bytes directly or the Handler writes to the Session's Channel.
        // In the Domain Entity I created earlier, I kept AudioStreamChannel.
        
        Console.WriteLine($"📤 HANDLER: Writing {audioBytes.Length} bytes to AudioStreamChannel for {request.ConnectionId}");
        var writeResult = session.AudioStreamChannel.Writer.TryWrite(audioBytes);
        Console.WriteLine($"🔍 HANDLER: TryWrite result: {writeResult} for {request.ConnectionId}");
        
        if (writeResult)
        {
            Console.WriteLine($"✅ HANDLER: Successfully wrote audio chunk to channel for {request.ConnectionId}");
        }
        else
        {
            Console.WriteLine($"❌ HANDLER: Failed to write audio chunk to channel for {request.ConnectionId} - channel may be completed or full");
        }
        
        // 3. Save Session (if we persisted audio bytes)
        // await _sessionRepository.SaveAsync(session, cancellationToken);

        return writeResult;
    }
}
