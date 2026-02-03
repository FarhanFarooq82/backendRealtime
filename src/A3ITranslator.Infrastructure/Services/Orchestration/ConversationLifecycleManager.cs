using System.Text;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.DTOs.Summary;
using A3ITranslator.Application.Enums;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Infrastructure.Services.Translation;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class ConversationLifecycleManager : IConversationLifecycleManager
{
    private readonly ILogger<ConversationLifecycleManager> _logger;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISpeakerManagementService _speakerManager;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ITranslationOrchestrator _translationOrchestrator;

    public ConversationLifecycleManager(
        ILogger<ConversationLifecycleManager> logger,
        ISessionRepository sessionRepository,
        ISpeakerManagementService speakerManager,
        IRealtimeNotificationService notificationService,
        ITranslationOrchestrator translationOrchestrator)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
        _speakerManager = speakerManager;
        _notificationService = notificationService;
        _translationOrchestrator = translationOrchestrator;
    }

    public async Task InitializeSessionAsync(string connectionId, string sessionId, string primaryLanguage, string secondaryLanguage)
    {
        await Task.CompletedTask;
    }

    public async Task CleanupConnectionAsync(string connectionId)
    {
        var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
        await _sessionRepository.RemoveByConnectionIdAsync(connectionId, CancellationToken.None);

        if (session != null)
        {
            await _speakerManager.ClearSessionAsync(session.SessionId);
            _logger.LogInformation("🧹 Cleaned up session {SessionId} for {ConnectionId}", session.SessionId, connectionId);
        }
    }

    public async Task RequestSummaryAsync(string connectionId)
    {
        try
        {
            _logger.LogInformation("🚀 Generating bilingual summary for {ConnectionId}", connectionId);
            
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogWarning("⚠️ Session not found for {ConnectionId}", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, "Session not found.");
                return;
            }

            if (session.ConversationHistory.Count == 0)
            {
                _logger.LogWarning("⚠️ No conversation history for {ConnectionId}", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, 
                    "No conversation history available.");
                return;
            }

            // Build single-language contexts (token efficient - no duplication)
            var primaryContext = BuildSingleLanguageContext(
                session.ConversationHistory.ToList(), 
                session.PrimaryLanguage);
            
            var secondaryContext = BuildSingleLanguageContext(
                session.ConversationHistory.ToList(), 
                session.SecondaryLanguage ?? "en-US");

            _logger.LogDebug("📊 Context sizes - Primary: {PrimaryTokens} chars, Secondary: {SecondaryTokens} chars",
                primaryContext.Length, secondaryContext.Length);

            // Generate BOTH summaries in PARALLEL (faster!)
            var primaryTask = _translationOrchestrator.GenerateSummaryInLanguageAsync(
                primaryContext, session.PrimaryLanguage);
            
            var secondaryTask = _translationOrchestrator.GenerateSummaryInLanguageAsync(
                secondaryContext, session.SecondaryLanguage ?? "en-US");

            await Task.WhenAll(primaryTask, secondaryTask);

            // Create structured DTO with RTL support
            var summaryDTO = new SessionSummaryDTO
            {
                Primary = new SummarySection
                {
                    Language = session.PrimaryLanguage,
                    LanguageName = LanguageConfigurationService.GetLanguageDisplayName(session.PrimaryLanguage),
                    IsRTL = LanguageConfigurationService.IsRightToLeft(session.PrimaryLanguage),
                    Content = primaryTask.Result
                },
                Secondary = new SummarySection
                {
                    Language = session.SecondaryLanguage ?? "en-US",
                    LanguageName = LanguageConfigurationService.GetLanguageDisplayName(session.SecondaryLanguage ?? "en-US"),
                    IsRTL = LanguageConfigurationService.IsRightToLeft(session.SecondaryLanguage ?? "en-US"),
                    Content = secondaryTask.Result
                },
                GeneratedAt = DateTime.UtcNow,
                TotalTurns = session.ConversationHistory.Count,
                MeetingDuration = DateTime.UtcNow - session.StartTime
            };

            await _notificationService.SendStructuredSummaryAsync(connectionId, summaryDTO);
            _logger.LogInformation("✅ Bilingual summary sent to {ConnectionId} ({TotalTurns} turns)", 
                connectionId, summaryDTO.TotalTurns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to generate summary for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, $"Summary failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds conversation context in a single language to avoid token duplication
    /// </summary>
    private string BuildSingleLanguageContext(List<ConversationTurn> history, string targetLanguage)
    {
        var context = new StringBuilder();
        
        foreach (var turn in history.OrderBy(t => t.SequenceNumber))
        {
            // Use original text if it matches target language, else use translation
            string text = turn.Language == targetLanguage 
                ? turn.OriginalText 
                : (turn.TranslatedText ?? turn.OriginalText);
            
            if (!string.IsNullOrWhiteSpace(text))
            {
                context.AppendLine($"[{turn.Timestamp:HH:mm:ss}] {turn.SpeakerName}: {text}");
            }
        }
        
        return context.ToString();
    }

    public async Task FinalizeAndMailAsync(string connectionId, List<string> emailAddresses)
    {
        try
        {
            _logger.LogInformation("🚀 LIFECYCLE: Finalizing session and mailing for {ConnectionId} to {Count} addresses", 
                connectionId, emailAddresses.Count);
            
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogWarning("⚠️ LIFECYCLE: Session not found for {ConnectionId}, cannot finalize", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, "Session not found.");
                return;
            }

            // Mock PDF generation and mailing
            _logger.LogInformation("📄 MOCK: Generating PDF for session {SessionId} with {TurnCount} turns", 
                session.SessionId, session.ConversationHistory.Count);
            
            foreach (var email in emailAddresses)
            {
                _logger.LogInformation("📧 MOCK: Sending transcript PDF to {Email}", email);
            }
            
            await _notificationService.SendFinalizationSuccessAsync(connectionId);
            _logger.LogInformation("✅ LIFECYCLE: Finalization successful for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ LIFECYCLE: Failed to finalize session for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, $"Finalization failed: {ex.Message}");
        }
    }
}
