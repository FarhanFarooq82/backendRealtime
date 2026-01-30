using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Domain.Entities;
using A3ITranslator.Application.Enums;
using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services.Speaker;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class SpeakerSyncService : ISpeakerSyncService
{
    private readonly ILogger<SpeakerSyncService> _logger;
    private readonly IAudioFeatureExtractor _featureExtractor;
    private readonly ISpeakerManagementService _speakerManager;
    private readonly ISpeakerIdentificationService _speakerService;

    public SpeakerSyncService(
        ILogger<SpeakerSyncService> logger,
        IAudioFeatureExtractor featureExtractor,
        ISpeakerManagementService speakerManager,
        ISpeakerIdentificationService speakerService)
    {
        _logger = logger;
        _featureExtractor = featureExtractor;
        _speakerManager = speakerManager;
        _speakerService = speakerService;
    }

    public async Task<(string? speakerId, string? displayName, float confidence, AudioFingerprint? fingerprint)> IdentifySpeakerAsync(
        string sessionId, 
        string connectionId, 
        IAsyncEnumerable<byte[]> audioStream, 
        CancellationToken cancellationToken)
    {
        var embedding = await _featureExtractor.ExtractEmbeddingAsync(connectionId);
        if (embedding == null || embedding.Length == 0)
        {
            return (null, null, 0f, null);
        }

        var candidates = await _speakerManager.GetSessionSpeakersAsync(sessionId);
        var fingerprint = new AudioFingerprint { Embedding = embedding };
        var scorecard = _speakerService.CompareFingerprints(fingerprint, candidates);
        
        var bestMatch = scorecard.FirstOrDefault();
        if (bestMatch != null && bestMatch.SimilarityScore > 0.75f)
        {
            return (bestMatch.SpeakerId, bestMatch.DisplayName, (float)bestMatch.SimilarityScore, fingerprint);
        }

        return (null, null, 0f, fingerprint);
    }

    public async Task<SpeakerGender> GetSpeakerGenderAsync(string sessionId, string speakerId)
    {
        var sessionSpeakers = await _speakerManager.GetSessionSpeakersAsync(sessionId);
        var speaker = sessionSpeakers?.FirstOrDefault(s => s.SpeakerId == speakerId);
        return speaker?.Gender ?? SpeakerGender.Unknown;
    }

    public async Task<SpeakerOperationResult> IdentifySpeakerAfterUtteranceAsync(string sessionId, SpeakerServicePayload payload)
    {
        return await _speakerManager.ProcessSpeakerIdentificationAsync(sessionId, payload);
    }
}
