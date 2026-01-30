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

    Task SendToTTSAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language);
    Task SendToTTSContinuousAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language);
}
