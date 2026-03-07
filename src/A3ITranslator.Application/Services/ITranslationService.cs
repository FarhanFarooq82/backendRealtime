using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Models.Conversation;

namespace A3ITranslator.Application.Services;

public interface ITranslationService
{
    Task<EnhancedTranslationRequest> CreateTranslationRequestAsync(string sessionId, UtteranceWithContext utterance, string? lastSpeakerId, string? provisionalSpeakerId, string? provisionalDisplayName, string turnId,
        bool isPremium = true);
}
