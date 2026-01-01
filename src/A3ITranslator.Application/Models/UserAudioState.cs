using System.Threading.Channels;

namespace A3ITranslator.Application.Models;

/// <summary>
/// Legacy model - use ConversationSession instead
/// Keeping for compatibility with existing code
/// </summary>
public class UserAudioState : IDisposable
{
    public string ConnectionId { get; set; } = string.Empty;
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string? CurrentSpeakerId { get; set; }
    public bool IsLanguageConfirmed { get; set; }
    public string? KnownSpeakerLanguage { get; set; }
    public string FinalTranscript { get; set; } = string.Empty;
    public Channel<byte[]> AudioStreamChannel { get; set; } = Channel.CreateUnbounded<byte[]>();
    public SpeakerRegistry SpeakerRegistry { get; set; } = new(); // Fixed property name
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    
    // Additional properties needed by other services
    public List<byte> AudioBuffer { get; set; } = new();

    // Add missing properties that other services expect
    public SpeakerRegistry Speakers => SpeakerRegistry; // Alias for compatibility

    public string GetDetectedOrDefaultLanguage()
    {
        return KnownSpeakerLanguage ?? "en";
    }

    public void Dispose()
    {
        AudioStreamChannel.Writer.Complete();
    }
}

public class SpeakerRegistry
{
    private readonly Dictionary<string, SpeakerProfile> _speakers = new();
    
    public SpeakerProfile? GetSpeaker(string speakerId) => _speakers.GetValueOrDefault(speakerId);
    
    public void AddSpeaker(string speakerId, VoiceCharacteristics characteristics, string? knownLanguage = null)
    {
        _speakers[speakerId] = new SpeakerProfile
        {
            SpeakerId = speakerId,
            VoiceCharacteristics = characteristics,
            KnownLanguage = knownLanguage,
            FirstSeen = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        };
    }
    
    public string? FindMatchingSpeaker(VoiceCharacteristics characteristics, float threshold = 0.8f)
    {
        return _speakers.Values
            .Where(s => CalculateSimilarity(characteristics, s.VoiceCharacteristics) >= threshold)
            .OrderByDescending(s => CalculateSimilarity(characteristics, s.VoiceCharacteristics))
            .FirstOrDefault()?.SpeakerId;
    }
    
    public IEnumerable<SpeakerProfile> GetAllSpeakers() => _speakers.Values;
    
    public void UpdateSpeakerLanguage(string speakerId, string language)
    {
        if (_speakers.TryGetValue(speakerId, out var speaker))
        {
            speaker.KnownLanguage = language;
            speaker.LastSeen = DateTime.UtcNow;
        }
    }
    
    private static float CalculateSimilarity(VoiceCharacteristics a, VoiceCharacteristics b)
    {
        return 1.0f - Math.Abs(a.Pitch - b.Pitch) / Math.Max(a.Pitch, b.Pitch);
    }
}

public class SpeakerProfile
{
    public string SpeakerId { get; set; } = string.Empty;
    public string? KnownLanguage { get; set; }
    public VoiceCharacteristics VoiceCharacteristics { get; set; } = new();
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string DisplayName { get; set; } = "Unknown Speaker";
    public float IdentificationConfidence { get; set; } = 1.0f;
}

public record VoiceCharacteristics(
    float Pitch = 150f, 
    float Energy = 0.5f, 
    string Gender = "Unknown",
    float[]? Formants = null)
{
    public float[] Formants { get; init; } = Formants ?? Array.Empty<float>();
}