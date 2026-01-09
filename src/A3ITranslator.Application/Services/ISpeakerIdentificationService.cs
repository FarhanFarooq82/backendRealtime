using A3ITranslator.Application.Models.SpeakerProfiles;

namespace A3ITranslator.Application.Services;

public interface ISpeakerIdentificationService
{
    /// <summary>
    /// Identify speaker from raw audio data (typically the first 2 seconds)
    /// </summary>
    Task<string> IdentifySpeakerAsync(byte[] audioData, string sessionId);
}