using A3ITranslator.Application.Models;
using A3ITranslator.Application.Domain.Entities;

namespace A3ITranslator.Application.Services;

public interface ISpeakerIdentificationService
{
    Task<string> IdentifyOrCreateSpeakerAsync(byte[] audioData, string sessionId);
    Task<string> IdentifySpeakerAsync(byte[] audioData, string sessionId); // ✅ Add this method
    Task<SpeakerProfile> AnalyzeSpeakerAsync(byte[] audioData, string transcript);
    VoiceCharacteristics ExtractVoiceCharacteristics(byte[] audioData);
    Task<string?> FindMatchingSpeakerAsync(VoiceCharacteristics characteristics, ConversationSession session);
}