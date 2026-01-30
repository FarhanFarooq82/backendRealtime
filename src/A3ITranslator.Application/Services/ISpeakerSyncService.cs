using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Services.Speaker;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service responsible for speaker identification and synchronization
/// </summary>
public interface ISpeakerSyncService
{
    /// <summary>
    /// Identifies the speaker for a given audio stream
    /// </summary>
    Task<(string? speakerId, string? displayName, float confidence, AudioFingerprint? fingerprint)> IdentifySpeakerAsync(
        string sessionId, 
        string connectionId, 
        IAsyncEnumerable<byte[]> audioStream, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the speaker gender for TTS voice selection
    /// </summary>
    Task<SpeakerGender> GetSpeakerGenderAsync(string sessionId, string speakerId);

    /// <summary>
    /// Syncs roster and handles identification decisions after an utterance
    /// </summary>
    Task<SpeakerOperationResult> IdentifySpeakerAfterUtteranceAsync(string sessionId, SpeakerServicePayload payload);
}
