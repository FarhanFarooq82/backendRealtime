using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

public interface IFactService
{
    Task StoreExtractedFactsAsync(string sessionId, EnhancedTranslationResponse genAIResponse);
}
