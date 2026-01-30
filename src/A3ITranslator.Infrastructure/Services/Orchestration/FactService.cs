using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;
using DomainConversationTurn = A3ITranslator.Application.Domain.Entities.ConversationTurn;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class FactService : IFactService
{
    private readonly ILogger<FactService> _logger;
    private readonly ISessionRepository _sessionRepository;

    public FactService(ILogger<FactService> logger, ISessionRepository sessionRepository)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
    }

    public async Task StoreExtractedFactsAsync(string sessionId, EnhancedTranslationResponse genAIResponse)
    {
        try
        {
            if (genAIResponse?.FactExtraction?.Facts != null && genAIResponse.FactExtraction.Facts.Any())
            {
                var session = await _sessionRepository.GetByIdAsync(sessionId, CancellationToken.None);
                if (session != null)
                {
                    var factTurn = DomainConversationTurn.CreateSpeech(
                        "system", 
                        "System", 
                        $"Extracted {genAIResponse.FactExtraction.Facts.Count} facts from conversation", 
                        "en"
                    ).SetMetadata("extractedFacts", genAIResponse.FactExtraction.Facts);
                    
                    session.AddConversationTurn(factTurn);
                    await _sessionRepository.SaveAsync(session, CancellationToken.None);
                    _logger.LogInformation($"Stored {genAIResponse.FactExtraction.Facts.Count} extracted facts for session {sessionId}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store extracted facts for session {SessionId}", sessionId);
        }
    }
}
