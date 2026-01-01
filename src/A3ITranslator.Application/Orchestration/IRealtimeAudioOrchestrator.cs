namespace A3ITranslator.Application.Orchestration;

public interface IRealtimeAudioOrchestrator
{
    Task ProcessAudioChunkAsync(string connectionId, string base64Chunk);
    Task<string> CommitAndProcessAsync(string connectionId, string language);
    void CleanupAudioDebug(string connectionId); // 🎵 DEBUG: Audio debug cleanup
}
