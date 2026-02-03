using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services.Speaker;

namespace A3ITranslator.Application.Services;

public interface IConversationResponseService
{
    Task SendResponseAsync(
        string connectionId,
        string sessionId,
        string? lastSpeakerId,
        UtteranceWithContext utterance,
        EnhancedTranslationResponse translationResponse,
        SpeakerOperationResult speakerUpdate);

    Task SendPulseAudioOnlyAsync(string connectionId, string sessionId, string? lastSpeakerId, EnhancedTranslationResponse pulseResponse);
    Task SendToTTSAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language, string estimatedGender = "Unknown", bool isPremium = true);
    Task SendToTTSContinuousAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language, string estimatedGender = "Unknown", bool isPremium = true);
}
