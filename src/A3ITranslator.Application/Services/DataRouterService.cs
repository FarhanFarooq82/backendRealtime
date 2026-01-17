using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Services.Speaker;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Services;

/// <summary>
/// SOLID Data Router Service - Routes enhanced translation response data to appropriate services
/// Single Responsibility: Route different payload types for maximum performance
/// </summary>
public interface IDataRouterService
{
    /// <summary>
    /// Process enhanced translation response and route data to appropriate services
    /// Returns aggregated results for frontend notification
    /// </summary>
    Task<RoutedDataResult> RouteTranslationDataAsync(
        string sessionId, 
        string connectionId, 
        EnhancedTranslationResponse response);
}

/// <summary>
/// Result of data routing operation
/// </summary>
public class RoutedDataResult
{
    public bool Success { get; set; } = true;
    public FrontendTranslationNotification FrontendNotification { get; set; } = new();
    public TTSServicePayload? TTSPayload { get; set; }
    public List<string> ProcessedServices { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Clean Data Router Implementation
/// Routes data efficiently to minimize network overhead and processing time
/// </summary>
public class DataRouterService : IDataRouterService
{
    private readonly ISpeakerManagementService _speakerService;
    private readonly ILogger<DataRouterService> _logger;

    public DataRouterService(
        ISpeakerManagementService speakerService,
        ILogger<DataRouterService> logger)
    {
        _speakerService = speakerService;
        _logger = logger;
    }

    public async Task<RoutedDataResult> RouteTranslationDataAsync(
        string sessionId, 
        string connectionId, 
        EnhancedTranslationResponse response)
    {
        var result = new RoutedDataResult();
        
        try
        {
            _logger.LogDebug("🚀 Routing translation data for session {SessionId}", sessionId);

            // 1. Route Speaker Data to Speaker Service
            var speakerResult = await RouteSpeakerDataAsync(sessionId, response);
            if (speakerResult.Success)
            {
                result.ProcessedServices.Add("SpeakerService");
            }
            else if (!string.IsNullOrEmpty(speakerResult.ErrorMessage))
            {
                result.Errors.Add($"SpeakerService: {speakerResult.ErrorMessage}");
            }

            // 2. Route Fact Data (if needed)
            if (response.FactExtraction.RequiresFactExtraction)
            {
                await RouteFactDataAsync(sessionId, speakerResult.SpeakerId, response);
                result.ProcessedServices.Add("FactService");
            }

            // 3. Prepare TTS Payload (if translation exists)
            if (!string.IsNullOrEmpty(response.Translation))
            {
                result.TTSPayload = new TTSServicePayload
                {
                    Text = response.Intent == "AI_ASSISTANCE" && !string.IsNullOrEmpty(response.AIAssistance.ResponseTranslated) 
                        ? response.AIAssistance.ResponseTranslated 
                        : response.Translation,
                    TargetLanguage = response.TranslationLanguage,
                    SpeakerId = speakerResult.SpeakerId,
                    SessionId = sessionId
                };
            }

            // 4. Build Frontend Notification
            result.FrontendNotification = BuildFrontendNotification(response, speakerResult, connectionId);
            
            result.Success = result.Errors.Count == 0;
            
            _logger.LogInformation("✅ Data routing completed: {Services} services processed, {Errors} errors", 
                result.ProcessedServices.Count, result.Errors.Count);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to route translation data for session {SessionId}", sessionId);
            result.Success = false;
            result.Errors.Add($"Routing failed: {ex.Message}");
            return result;
        }
    }

    private async Task<SpeakerOperationResult> RouteSpeakerDataAsync(
        string sessionId, 
        EnhancedTranslationResponse response)
    {
        try
        {
            var speakerPayload = new SpeakerServicePayload
            {
                SessionId = sessionId,
                Identification = response.SpeakerIdentification,
                ProfileUpdate = response.SpeakerProfileUpdate,
                AudioLanguage = response.AudioLanguage,
                TranscriptionConfidence = response.Confidence
            };

            return await _speakerService.ProcessSpeakerIdentificationAsync(sessionId, speakerPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to route speaker data for session {SessionId}", sessionId);
            return new SpeakerOperationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SpeakerId = "unknown"
            };
        }
    }

    private async Task RouteFactDataAsync(
        string sessionId, 
        string speakerId, 
        EnhancedTranslationResponse response)
    {
        try
        {
            // TODO: Implement fact service routing when fact management service is created
            // For now, just log that facts were detected
            if (response.FactExtraction.Facts.Count > 0)
            {
                _logger.LogInformation("📝 Facts detected for session {SessionId}: {FactCount} facts", 
                    sessionId, response.FactExtraction.Facts.Count);
                    
                foreach (var fact in response.FactExtraction.Facts.Take(3))
                {
                    _logger.LogDebug("  - {Fact}", fact);
                }
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to route fact data for session {SessionId}", sessionId);
        }
    }

    private FrontendTranslationNotification BuildFrontendNotification(
        EnhancedTranslationResponse response, 
        SpeakerOperationResult speakerResult,
        string connectionId)
    {
        // Get speaker name from speaker service if available
        var speakerName = speakerResult.Success && !string.IsNullOrEmpty(speakerResult.SpeakerId) && speakerResult.SpeakerId != "unknown"
            ? response.SpeakerProfileUpdate?.SuggestedName ?? speakerResult.SpeakerId
            : "Unknown Speaker";

        var notification = new FrontendTranslationNotification
        {
            OriginalText = response.ImprovedTranscription,
            TranslatedText = response.Translation,
            SourceLanguage = response.AudioLanguage,
            TargetLanguage = response.TranslationLanguage,
            SpeakerId = speakerResult.SpeakerId,
            SpeakerName = speakerName,
            Confidence = response.Confidence,
            Intent = response.Intent,
            Timestamp = DateTime.UtcNow
        };

        // Add AI response if triggered
        if (response.AIAssistance.TriggerDetected)
        {
            notification.AIResponse = response.AIAssistance.Response;
            notification.AIResponseTranslated = response.AIAssistance.ResponseTranslated;
        }

        return notification;
    }
}
