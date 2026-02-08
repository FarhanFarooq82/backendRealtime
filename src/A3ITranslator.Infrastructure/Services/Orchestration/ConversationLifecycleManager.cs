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
            // Create structured DTO with RTL support
            var summaryDTO = new SessionSummaryDTO
            {
                Primary = ParseSummaryResponse(primaryTask.Result, session.PrimaryLanguage),
                Secondary = ParseSummaryResponse(secondaryTask.Result, session.SecondaryLanguage ?? "en-US"),
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

    private SummarySection ParseSummaryResponse(string rawText, string language)
    {
        var section = new SummarySection
        {
            Language = language,
            LanguageName = LanguageConfigurationService.GetLanguageDisplayName(language),
            IsRTL = LanguageConfigurationService.IsRightToLeft(language)
        };

        try 
        {
            var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .Where(l => !string.IsNullOrWhiteSpace(l))
                              .ToList();

            // 1. Extract Labels
            foreach (var line in lines)
            {
                if (line.StartsWith("LabelDate:", StringComparison.OrdinalIgnoreCase)) section.LabelDate = line.Substring("LabelDate:".Length).Trim();
                else if (line.StartsWith("LabelLocation:", StringComparison.OrdinalIgnoreCase)) section.LabelLocation = line.Substring("LabelLocation:".Length).Trim();
                else if (line.StartsWith("LabelTitle:", StringComparison.OrdinalIgnoreCase)) section.LabelTitle = line.Substring("LabelTitle:".Length).Trim();
                else if (line.StartsWith("LabelObjective:", StringComparison.OrdinalIgnoreCase)) section.LabelObjective = line.Substring("LabelObjective:".Length).Trim();
                else if (line.StartsWith("LabelParticipants:", StringComparison.OrdinalIgnoreCase)) section.LabelParticipants = line.Substring("LabelParticipants:".Length).Trim();
                else if (line.StartsWith("LabelKeyDiscussionPoints:", StringComparison.OrdinalIgnoreCase)) section.LabelKeyDiscussionPoints = line.Substring("LabelKeyDiscussionPoints:".Length).Trim();
                else if (line.StartsWith("LabelActionItems:", StringComparison.OrdinalIgnoreCase)) section.LabelActionItems = line.Substring("LabelActionItems:".Length).Trim();
            }
            
            // 2. Extract Data
            int currentSection = 0; // 0=None, 1=Participants, 2=KeyPoints, 3=Actions
            
            foreach (var line in lines)
            {
                if (line.StartsWith("Label")) continue;

                // Check for single-line fields
                if (!string.IsNullOrEmpty(section.LabelDate) && line.StartsWith(section.LabelDate, StringComparison.OrdinalIgnoreCase)) 
                {
                    section.Date = ExtractValue(line, section.LabelDate);
                    currentSection = 0;
                }
                else if (!string.IsNullOrEmpty(section.LabelLocation) && line.StartsWith(section.LabelLocation, StringComparison.OrdinalIgnoreCase))
                {
                    section.Location = ExtractValue(line, section.LabelLocation);
                    currentSection = 0;
                }
                else if (!string.IsNullOrEmpty(section.LabelTitle) && line.StartsWith(section.LabelTitle, StringComparison.OrdinalIgnoreCase))
                {
                    section.Title = ExtractValue(line, section.LabelTitle);
                    currentSection = 0;
                }
                else if (!string.IsNullOrEmpty(section.LabelObjective) && line.StartsWith(section.LabelObjective, StringComparison.OrdinalIgnoreCase))
                {
                    section.Objective = ExtractValue(line, section.LabelObjective);
                    currentSection = 0;
                }
                
                // Check for list section headers
                else if (!string.IsNullOrEmpty(section.LabelParticipants) && line.Contains(section.LabelParticipants, StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = 1;
                }
                else if (!string.IsNullOrEmpty(section.LabelKeyDiscussionPoints) && line.Contains(section.LabelKeyDiscussionPoints, StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = 2;
                }
                else if (!string.IsNullOrEmpty(section.LabelActionItems) && line.Contains(section.LabelActionItems, StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = 3;
                }
                
                // List items
                else if (line.StartsWith("-") || line.StartsWith("*"))
                {
                    var item = line.TrimStart('-', '*', ' ').Trim();
                    if (currentSection == 1) section.Participants.Add(item);
                    else if (currentSection == 2) section.KeyDiscussionPoints.Add(item);
                    else if (currentSection == 3) section.ActionItems.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing structured summary for {Lang}", language);
            section.Title = "Summary Parser Error";
            section.Objective = rawText; 
        }

        return section;
    }

    private string ExtractValue(string line, string label)
    {
        var combined = label + ":";
        if (line.StartsWith(combined, StringComparison.OrdinalIgnoreCase))
            return line.Substring(combined.Length).Trim();
            
        if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
             return line.Substring(label.Length).Trim();
             
        return line;
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
