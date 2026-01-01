using A3ITranslator.Application.DTOs.Audio;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service for extracting speaker characteristics from transcription content
/// Combines pitch analysis with text-based analysis for comprehensive speaker profiling
/// </summary>
public interface ISpeakerCharacteristicsService
{
    // Existing methods...
    Speaker ExtractSpeakingPatterns(Speaker speaker, string transcription, TimeSpan audioDuration);
    SpeakerMatchResult CheckExistingSpeaker(Speaker newSpeaker, List<Speaker> sessionSpeakers);
    string GenerateSpeakerName(Speaker speaker, string baseLanguage, int speakerNumber);

    // NEW: Add voice feature extraction integration
    Task<Speaker> CreateSpeakerWithVoiceAnalysisAsync(
        byte[] audioData, 
        int sampleRate, 
        float durationSeconds,
        STTResult sttResult, 
        string sessionId);
}
