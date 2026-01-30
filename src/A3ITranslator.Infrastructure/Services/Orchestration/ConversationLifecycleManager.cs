using System.Text;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Enums;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.Domain.Interfaces;
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
            _logger.LogInformation("🚀 LIFECYCLE: Generating session summary for {ConnectionId}", connectionId);
            
            var session = await _sessionRepository.GetByConnectionIdAsync(connectionId, CancellationToken.None);
            if (session == null)
            {
                _logger.LogWarning("⚠️ LIFECYCLE: Session not found for {ConnectionId}, cannot generate summary", connectionId);
                await _notificationService.NotifyErrorAsync(connectionId, "Session not found.");
                return;
            }

            // Build history string from conversation turns
            var historyBuilder = new StringBuilder();
            foreach (var turn in session.ConversationHistory.OrderBy(t => t.Timestamp))
            {
                historyBuilder.AppendLine($"[{turn.Timestamp:HH:mm:ss}] {turn.SpeakerName}: {turn.OriginalText}");
                if (!string.IsNullOrEmpty(turn.TranslatedText))
                {
                    historyBuilder.AppendLine($"   (Translation: {turn.TranslatedText})");
                }
            }
            
            var history = historyBuilder.ToString();
            
            if (string.IsNullOrWhiteSpace(history))
            {
                await _notificationService.SendSessionSummaryAsync(connectionId, "No conversation history available to summarize.");
                return;
            }

            var summary = await _translationOrchestrator.GenerateConversationSummaryAsync(
                history, 
                session.PrimaryLanguage, 
                session.SecondaryLanguage ?? "en-US");

            await _notificationService.SendSessionSummaryAsync(connectionId, summary);
            _logger.LogInformation("✅ LIFECYCLE: Summary sent for {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ LIFECYCLE: Failed to handle summary request for {ConnectionId}", connectionId);
            await _notificationService.NotifyErrorAsync(connectionId, $"Summary generation failed: {ex.Message}");
        }
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
