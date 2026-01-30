using MediatR;
using A3ITranslator.Application.Domain.Events;
using A3ITranslator.Application.Services; // Ensure this contains service interfaces or adjust
using A3ITranslator.Application.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Features.Conversation.EventHandlers;

public class TranslationEventHandler : INotificationHandler<UtteranceCommitted>
{
    private readonly IGenAIService _genAIService;
    private readonly IFactExtractionService _factService;
    private readonly IPublisher _publisher;
    private readonly ISessionRepository _sessionRepository;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<TranslationEventHandler> _logger;

    public TranslationEventHandler(
        IGenAIService genAIService,
        IFactExtractionService factService,
        IPublisher publisher,
        ISessionRepository sessionRepository,
        IMetricsService metricsService,
        ILogger<TranslationEventHandler> logger)
    {
        _genAIService = genAIService;
        _factService = factService;
        _publisher = publisher;
        _sessionRepository = sessionRepository;
        _metricsService = metricsService;
        _logger = logger;
    }

    public async Task Handle(UtteranceCommitted notification, CancellationToken cancellationToken)
    {
        var session = notification.Session;
        var transcript = notification.Transcript;

        if (string.IsNullOrWhiteSpace(transcript)) return;

        try
        {
            _logger.LogInformation("🧠 Handling UtteranceCommitted for Session {SessionId}. Generating AI Response...", session.SessionId);

            // 1. Build Context
            string factContext = await _factService.BuildFactContextAsync(session.SessionId);
            string systemPrompt = $@"You are a helpful assistant. Use the following context to answer questions:
{factContext}

Provide clear, concise responses based on the conversation history and facts.";

            // 2. Generate Response
            var genAIResponse = await _genAIService.GenerateResponseAsync(systemPrompt, $"Speaker said: {transcript}");
            string responseText = genAIResponse.Content;

            // Log Metrics
            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                SessionId = session.SessionId,
                Category = ServiceCategory.Translation,
                Provider = _genAIService.GetServiceName(),
                Operation = "BackgroundTranslation",
                Model = genAIResponse.Model,
                InputUnits = genAIResponse.Usage.InputTokens,
                InputUnitType = "Tokens",
                OutputUnits = genAIResponse.Usage.OutputTokens,
                OutputUnitType = "Tokens",
                UserPrompt = transcript,
                Response = responseText,
                CostUSD = (genAIResponse.Usage.InputTokens * 0.0000025) + (genAIResponse.Usage.OutputTokens * 0.000010)
            });
            
            _logger.LogInformation("✅ AI Response Generated: {Response}", responseText);

            // 3. Update History (Add System Turn)
            // AI usually responds in Target Language or English. We assume 'en' for now or session target.
            // Using "system" as generic ID.
            var aiTurn = A3ITranslator.Application.Domain.Entities.ConversationTurn.CreateSpeech(
                "system",
                "AI Assistant",
                responseText,
                "en" 
            );
            session.AddConversationTurn(aiTurn);
            
            // Persist state
            await _sessionRepository.SaveAsync(session, cancellationToken);

            // 4. Publish Event to trigger TTS
            await _publisher.Publish(new TranslationCompleted(session, aiTurn, responseText, "en"), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response for Session {SessionId}", session.SessionId);
        }
    }
}
