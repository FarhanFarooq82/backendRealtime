using A3ITranslator.Application.DTOs.Speaker;

namespace A3ITranslator.Application.Interfaces; // Changed namespace

/// <summary>
/// Enhanced client interface with speaker support
/// </summary>
public interface IAudioClient
{
    Task ReceiveTranscription(string text, string language, bool isFinal);
    Task ReceiveAudioChunk(string base64Chunk);
    Task ReceiveSpeakerUpdate(SpeakerInfo speaker);
    Task ReceiveTransactionComplete();
    Task ReceiveError(string message);
    
    // Add language detection method
    Task ReceiveDominantLanguageDetected(string dominantLanguage);
}