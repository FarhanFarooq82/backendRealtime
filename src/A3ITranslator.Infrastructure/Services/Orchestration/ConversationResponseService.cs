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
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services.Azure;
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

            // 🚀 Step 2: Parallel TTS and Conversation Items
            var tasks = new List<Task>();
            
            // TTS Logic
            string ttsText = (translationResponse.AIAssistance.TriggerDetected && !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
                ? translationResponse.AIAssistance.Response ?? string.Empty
                : translationResponse.Translation ?? string.Empty;
            
            string ttsLanguage = (translationResponse.AIAssistance.TriggerDetected && !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
                ? translationResponse.AudioLanguage ?? "en"
                : translationResponse.TranslationLanguage ?? "en";

            if (!string.IsNullOrEmpty(ttsText))
            {
                tasks.Add(SendToTTSContinuousAsync(connectionId, sessionId, activeSpeaker.SpeakerId, ttsText, ttsLanguage));
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
                utterance.SpeakerConfidence
            );

            tasks.Add(Task.Run(async () =>
            {
                await _notificationService.SendFrontendConversationItemAsync(connectionId, mainItem);
                await AddToHistoryAsync(sessionId, mainItem, activeSpeaker.SpeakerId, translationResponse.AudioLanguage ?? "en-US");
            }));

            if (translationResponse.AIAssistance.TriggerDetected && !string.IsNullOrEmpty(translationResponse.AIAssistance.ResponseTranslated))
            {
                var aiItem = _frontendService.CreateFromAIResponse(
                    translationResponse.AudioLanguage ?? "en-US",
                    translationResponse.AIAssistance.Response ?? string.Empty,
                    translationResponse.AIAssistance.ResponseTranslated,
                    translationResponse.TranslationLanguage ?? "en",
                    1.0f
                );
                tasks.Add(Task.Run(async () =>
                {
                    await _notificationService.SendFrontendConversationItemAsync(connectionId, aiItem);
                    await AddToHistoryAsync(sessionId, aiItem, "ai-assistant", translationResponse.AudioLanguage ?? "en-US");
                }));
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in ConversationResponseService for {ConnectionId}", connectionId);
        }
    }

    public async Task SendToTTSAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language)
    {
        try
        {
            var speakerGender = SpeakerGender.Unknown;
            if (!string.IsNullOrEmpty(lastSpeakerId))
            {
                speakerGender = await _speakerSyncService.GetSpeakerGenderAsync(sessionId, lastSpeakerId);
            }

            if (_ttsService is AzureNeuralVoiceService neuralVoiceService)
            {
                await foreach (var chunk in neuralVoiceService.SynthesizeWithGenderAsync(
                    text, language, speakerGender, VoiceStyle.Conversational, isPremium: false))
                {
                    var chunkItem = _frontendService.CreateTTSChunk(
                        "neural-tts-" + Guid.NewGuid().ToString()[..8],
                        Convert.ToBase64String(chunk.AudioData),
                        chunk.AssociatedText,
                        chunk.ChunkIndex,
                        chunk.TotalChunks,
                        0.0,
                        "audio/mp3"
                    );
                    await _notificationService.SendFrontendTTSChunkAsync(connectionId, chunkItem);
                }
            }
            else
            {
                await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in SendToTTSAsync for {ConnectionId}", connectionId);
        }
    }

    public async Task SendToTTSContinuousAsync(string connectionId, string sessionId, string? lastSpeakerId, string text, string language)
    {
        try
        {
            var speakerGender = SpeakerGender.Unknown;
            if (!string.IsNullOrEmpty(lastSpeakerId))
            {
                speakerGender = await _speakerSyncService.GetSpeakerGenderAsync(sessionId, lastSpeakerId);
            }

            if (_ttsService is AzureNeuralVoiceService neuralVoiceService)
            {
                var conversationItemId = "continuous-tts-" + Guid.NewGuid().ToString()[..8];
                await foreach (var chunk in neuralVoiceService.SynthesizeWithGenderAsync(
                    text, language, speakerGender, VoiceStyle.Conversational, isPremium: false))
                {
                    var chunkItem = _frontendService.CreateTTSChunk(
                        conversationItemId,
                        Convert.ToBase64String(chunk.AudioData),
                        chunk.AssociatedText,
                        chunk.ChunkIndex,
                        chunk.TotalChunks,
                        0.0,
                        "audio/mp3"
                    );
                    await _notificationService.SendFrontendTTSChunkAsync(connectionId, chunkItem);
                }

                _ = _metricsService.LogMetricsAsync(new UsageMetrics
                {
                    SessionId = sessionId,
                    ConnectionId = connectionId,
                    Category = ServiceCategory.TTS,
                    Provider = "Azure",
                    Operation = "NeuralTTS",
                    OutputUnits = text.Length,
                    OutputUnitType = "Characters",
                    UserPrompt = text,
                    Response = "AUDIO_STREAM",
                    CostUSD = text.Length * 0.000016,
                    Status = "Success"
                });
            }
            else
            {
                await _ttsService.SynthesizeAndNotifyAsync(connectionId, text, language);
            }
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
