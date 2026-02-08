using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Frontend;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Enums;
using Microsoft.Extensions.Logging;
using DomainConversationTurn = A3ITranslator.Application.Domain.Entities.ConversationTurn;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class ConversationResponseService : IConversationResponseService
{
    private readonly ILogger<ConversationResponseService> _logger;
    private readonly IFrontendConversationItemService _frontendService;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISpeakerManagementService _speakerManager;
    private readonly IStreamingTTSService _ttsService;
    private readonly ISpeakerSyncService _speakerSyncService;
    private readonly IMetricsService _metricsService;

    public ConversationResponseService(
        ILogger<ConversationResponseService> logger,
        IFrontendConversationItemService frontendService,
        IRealtimeNotificationService notificationService,
        ISessionRepository sessionRepository,
        ISpeakerManagementService speakerManager,
        IStreamingTTSService ttsService,
        ISpeakerSyncService speakerSyncService,
        IMetricsService metricsService)
    {
        _logger = logger;
        _frontendService = frontendService;
        _notificationService = notificationService;
        _sessionRepository = sessionRepository;
        _speakerManager = speakerManager;
        _ttsService = ttsService;
        _speakerSyncService = speakerSyncService;
        _metricsService = metricsService;
    }

    public async Task SendResponseAsync(
        string connectionId,
        string sessionId,
        string? lastSpeakerId,
        UtteranceWithContext utterance,
        EnhancedTranslationResponse translationResponse,
        SpeakerOperationResult speakerUpdate)
    {
        try
        {
            _logger.LogDebug("🚀 RESPONSE: Starting parallel response processing for {ConnectionId}", connectionId);

            var speakers = await _speakerManager.GetSessionSpeakersAsync(sessionId);
            var activeSpeakerLookupId = speakerUpdate.SpeakerId ?? utterance.ProvisionalSpeakerId;
            var activeSpeaker = speakers.FirstOrDefault(s => s.SpeakerId == activeSpeakerLookupId) 
                ?? new SpeakerProfile { SpeakerId = "unknown", DisplayName = "Unknown Speaker" };

            if (!string.IsNullOrEmpty(speakerUpdate.DisplayName))
            {
                activeSpeaker.DisplayName = speakerUpdate.DisplayName;
            }

            // 🎭 Step 1: Sequential Speaker Update
            if (speakerUpdate.Success)
            {
                var frontendSpeakerUpdate = _frontendService.CreateSpeakerListUpdate(speakers);
                await _notificationService.SendFrontendSpeakerListAsync(connectionId, frontendSpeakerUpdate);
            }

            // 🎭 Step 2: Parallel TTS and Conversation Items
            var tasks = new List<Task>();
            
            // TTS Logic: In this new merged mode, Pulse TTS is handled separately.
            // Brain TTS only happens for AI_ASSISTANCE.
            bool shouldStreamTTS = !translationResponse.IsPulse && translationResponse.Intent == "AI_ASSISTANCE";

            if (shouldStreamTTS)
            {
                // User Request: "ai responce should take the ai responce not translated text for tts and the voice could be default and the languge should be audiolanguage"
                // Logic:
                // If Intent == AI_ASSISTANCE:
                //   - TTS Text = translationResponse.AIAssistance.Response (The answer in source lang)
                //   - TTS Lang = translationResponse.AudioLanguage (Source lang)
                // Else (Translation):
                //   - TTS Text = translationResponse.Translation
                //   - TTS Lang = translationResponse.TranslationLanguage
                string ttsText = (translationResponse.Intent == "AI_ASSISTANCE")
                    ? translationResponse.AIAssistance.Response ?? string.Empty
                    : translationResponse.Translation ?? string.Empty;
                
                string ttsLanguage = (translationResponse.Intent == "AI_ASSISTANCE")
                    ? translationResponse.AudioLanguage ?? "en" 
                    : translationResponse.TranslationLanguage ?? "en";

                if (!string.IsNullOrEmpty(ttsText))
                {
                    // For AI_ASSISTANCE, use "System" or null so we don't mimic the user's voice.
                    // This ensures the voice is selected based purely on language and gender (defaulting to Female Assistant).
                    string? ttsSpeakerId = (translationResponse.Intent == "AI_ASSISTANCE") ? null : activeSpeaker.SpeakerId;
                    
                    tasks.Add(SendToTTSContinuousAsync(connectionId, sessionId, ttsSpeakerId, ttsText, ttsLanguage, translationResponse.EstimatedGender, isPremium: true));
                }
            }

            // Create Conversation Items
            var mainItem = _frontendService.CreateFromTranslation(
                new UtteranceWithContext
                {
                    Text = translationResponse.ImprovedTranscription,
                    DominantLanguage = translationResponse.AudioLanguage ?? "en-US",
                    TranscriptionConfidence = utterance.TranscriptionConfidence,
                    ProvisionalSpeakerId = utterance.ProvisionalSpeakerId,
                    AudioFingerprint = utterance.AudioFingerprint,
                    CreatedAt = utterance.CreatedAt
                },
                translationResponse.Translation ?? "",
                translationResponse.TranslationLanguage ?? "en",
                translationResponse.Confidence,
                activeSpeaker,
                utterance.SpeakerConfidence,
                translationResponse.TurnId,
                translationResponse.IsPulse,
                translationResponse.FactExtraction.HasSignificantInfo
            );

            tasks.Add(Task.Run(async () =>
            {
                await _notificationService.SendFrontendConversationItemAsync(connectionId, mainItem);
                
                // 🛑 HISTORY GUARD: Only add to database history for the FINAL (Brain) response
                if (!translationResponse.IsPulse)
                {
                    await AddToHistoryAsync(sessionId, mainItem, activeSpeaker.SpeakerId, translationResponse.AudioLanguage ?? "en-US");
                }
            }));

            if (translationResponse.Intent == "AI_ASSISTANCE" && !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
            {
                var aiItem = _frontendService.CreateFromAIResponse(
                    translationResponse.AudioLanguage ?? "en-US",
                    translationResponse.AIAssistance.Response ?? string.Empty,
                    translationResponse.AIAssistance.ResponseTranslated,
                    translationResponse.TranslationLanguage ?? "en",
                    1.0f,
                    translationResponse.TurnId + "_ai" // Correlate AI sub-item
                );
                tasks.Add(Task.Run(async () =>
                {
                    await _notificationService.SendFrontendConversationItemAsync(connectionId, aiItem);
                    
                    if (!translationResponse.IsPulse)
                    {
                        await AddToHistoryAsync(sessionId, aiItem, "ai-assistant", translationResponse.AudioLanguage ?? "en-US");
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in ConversationResponseService for {ConnectionId}", connectionId);
        }
    }

    public async Task SendPulseAudioOnlyAsync(string connectionId, string sessionId, string? lastSpeakerId, EnhancedTranslationResponse pulseResponse)
    {
        try
        {
            if (pulseResponse.Intent == "SIMPLE_TRANSLATION" && !string.IsNullOrEmpty(pulseResponse.Translation))
            {
                _logger.LogDebug("⚡ PULSE TTS: Streaming fast audio for {ConnectionId} with gender {Gender}", connectionId, pulseResponse.EstimatedGender);
                await SendToTTSContinuousAsync(connectionId, sessionId, lastSpeakerId, pulseResponse.Translation, pulseResponse.TranslationLanguage ?? "en", pulseResponse.EstimatedGender, isPremium: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in SendPulseAudioOnlyAsync for {ConnectionId}", connectionId);
        }
    }

    public async Task SendToTTSAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language, string estimatedGender = "Unknown", bool isPremium = true)
    {
        try
        {
            await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language, lastSpeakerId, estimatedGender, isPremium);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in SendToTTSAsync for {ConnectionId}", connectionId);
        }
    }

    public async Task SendToTTSContinuousAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language, string estimatedGender = "Unknown", bool isPremium = true)
    {
        try
        {
            var voiceUsed = await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language, lastSpeakerId, estimatedGender, isPremium);

            // Cost Calculation: Neural ($16/1M) vs Standard ($4/1M)
            bool isNeural = voiceUsed.Contains("Neural") || (isPremium && !voiceUsed.Contains("Standard"));
            double costPerChar = isNeural ? 0.000016 : 0.000004;

            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                SessionId = sessionId,
                ConnectionId = connectionId,
                Category = ServiceCategory.TTS,
                Provider = "Azure",
                Operation = "StreamingTTS",
                Model = isNeural ? "Neural" : "Standard",
                VoiceUsed = voiceUsed,
                OutputUnits = text.Length,
                OutputUnitType = "Characters",
                UserPrompt = text,
                Response = "AUDIO_STREAM",
                CostUSD = text.Length * costPerChar,
                Status = "Success"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in SendToTTSContinuousAsync for {ConnectionId}", connectionId);
        }
    }

    private async Task AddToHistoryAsync(string sessionId, FrontendConversationItem conversationItem, string speakerId, string audioLanguage)
    {
        try
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId, CancellationToken.None);
            if (session != null)
            {
                var conversationTurn = DomainConversationTurn.CreateSpeech(
                    speakerId,
                    conversationItem.SpeakerName ?? "Unknown",
                    conversationItem.TranscriptionText ?? "",
                    audioLanguage
                ).SetTranslation(
                    conversationItem.TranslationText ?? "",
                    audioLanguage
                );

                conversationTurn.SetMetadata("frontendResponseType", conversationItem.ResponseType)
                              .SetMetadata("sentToFrontend", DateTime.UtcNow)
                              .SetMetadata("transcriptionConfidence", conversationItem.TranscriptionConfidence)
                              .SetMetadata("translationConfidence", conversationItem.TranslationConfidence)
                              .SetMetadata("speakerConfidence", conversationItem.SpeakerConfidence);

                session.AddConversationTurn(conversationTurn);
                await _sessionRepository.SaveAsync(session, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add frontend conversation to history for session {SessionId}", sessionId);
        }
    }
}
