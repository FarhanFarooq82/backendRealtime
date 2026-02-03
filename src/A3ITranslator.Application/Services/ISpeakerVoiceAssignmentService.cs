namespace A3ITranslator.Application.Services;

/// <summary>
/// Service responsible for assigning and managing Azure Neural voices for speakers.
/// Ensures each speaker gets a consistent, appropriate voice based on their characteristics.
/// </summary>
public interface ISpeakerVoiceAssignmentService
{
    /// <summary>
    /// Assigns an appropriate Azure Neural voice to a speaker based on their profile.
    /// If already assigned, returns the existing assignment for consistency.
    /// </summary>
    /// <param name="speakerId">Unique speaker identifier</param>
    /// <param name="targetLanguage">Target language for TTS (e.g., "en-US", "da-DK", "ur-PK")</param>
    /// <param name="gender">Speaker's detected gender</param>
    /// <param name="existingSpeakerVoices">Dictionary of already assigned voices to avoid duplicates</param>
    /// <returns>Azure Neural voice name (e.g., "en-US-JennyNeural")</returns>
    string AssignVoiceToSpeaker(
        string speakerId, 
        string targetLanguage, 
        string? gender = null,
        Dictionary<string, string>? existingSpeakerVoices = null);
    
    /// <summary>
    /// Gets the assigned voice for a speaker, or assigns one if not yet assigned.
    /// </summary>
    string GetOrAssignVoice(string speakerId, string targetLanguage, string? gender = null);
    
    /// <summary>
    /// Clears voice assignment for a speaker (e.g., when session ends)
    /// </summary>
    void ClearAssignment(string speakerId);
}
