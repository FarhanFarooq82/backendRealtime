using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Infrastructure.Services.Translation;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class TranslationService : ITranslationService
{
    private readonly ILogger<TranslationService> _logger;
    private readonly ISessionRepository _sessionRepository;
    private readonly ITranslationOrchestrator _translationOrchestrator;
    private readonly ISpeakerManagementService _speakerManager;
    private readonly ISpeakerIdentificationService _speakerService;

    public TranslationService(
        ILogger<TranslationService> logger,
        ISessionRepository sessionRepository,
        ITranslationOrchestrator translationOrchestrator,
        ISpeakerManagementService speakerManager,
        ISpeakerIdentificationService speakerService)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
        _translationOrchestrator = translationOrchestrator;
        _speakerManager = speakerManager;
        _speakerService = speakerService;
    }

    public async Task<EnhancedTranslationResponse> ProcessTranslationAsync(
        string sessionId, 
        UtteranceWithContext utterance, 
        string? lastSpeakerId, 
        string? provisionalSpeakerId, 
        string? provisionalDisplayName)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, CancellationToken.None);
        var recentHistory = new List<ConversationHistoryItem>();
        var existingFacts = new List<string>();
        
        if (session != null)
        {
             recentHistory = session.ConversationHistory
                .TakeLast(5)
                .Select(t => new ConversationHistoryItem 
                { 
                    SpeakerId = t.SpeakerId, 
                    SpeakerName = t.SpeakerName, 
                    Text = t.OriginalText 
                })
                .ToList();

            existingFacts = session.ConversationHistory
                .Where(t => t.Metadata.ContainsKey("extractedFacts"))
                .SelectMany(t => {
                    var facts = t.Metadata["extractedFacts"];
                    if (facts is IEnumerable<dynamic> dynList) return dynList;
                    if (facts is System.Collections.IEnumerable enm) return enm.Cast<object>();
                    return Enumerable.Empty<object>();
                })
                .Cast<object>() // 👈 FORCE NON-DYNAMIC
                .Select(f => f?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();
        }

        var request = new EnhancedTranslationRequest
        {
            Text = utterance.Text,
            SourceLanguage = utterance.SourceLanguage,
            TargetLanguage = utterance.TargetLanguage,
            SessionId = sessionId,
            SessionContext = new Dictionary<string, object>
            {
                ["sessionId"] = sessionId,
                ["lastSpeaker"] = lastSpeakerId ?? "None",
                ["provisionalId"] = provisionalSpeakerId ?? "Unknown",
                ["provisionalName"] = provisionalDisplayName ?? "Unknown",
                ["expectedLanguageCode"] = utterance.SourceLanguage,
                ["recentHistory"] = recentHistory,
                ["existingFacts"] = existingFacts
            }
        };

        if (utterance.AudioFingerprint != null)
        {
            var candidates = await _speakerManager.GetSessionSpeakersAsync(sessionId);
            var scorecard = _speakerService.CompareFingerprints(utterance.AudioFingerprint, candidates);
            request.SessionContext["speakerScorecard"] = scorecard;
            request.SessionContext["acousticDNA"] = utterance.AudioFingerprint;
        }

        return await _translationOrchestrator.ProcessEnhancedTranslationAsync(request);
    }
}
