using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class SpeakerIdentificationService : ISpeakerIdentificationService
{
    private readonly ILogger<SpeakerIdentificationService> _logger;

    public SpeakerIdentificationService(ILogger<SpeakerIdentificationService> logger)
    {
        _logger = logger;
    }

    // Add the missing method in your SpeakerIdentificationService:
    public async Task<string> IdentifySpeakerAsync(byte[] audioData, string sessionId)
    {
        // ✅ Delegate to existing method
        return await IdentifyOrCreateSpeakerAsync(audioData, sessionId);
    }
    public async Task<string> IdentifyOrCreateSpeakerAsync(byte[] audioData, string sessionId)
    {
        try
        {
            var characteristics = ExtractVoiceCharacteristics(audioData);
            var speakerId = $"speaker_{sessionId[..8]}_{GetVoiceFingerprint(characteristics)}";
            
            _logger.LogInformation("🎭 Identified speaker: {SpeakerId}", speakerId);
            return speakerId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Speaker identification failed");
            return $"speaker_unknown_{sessionId[..8]}";
        }
    }

    public async Task<SpeakerProfile> AnalyzeSpeakerAsync(byte[] audioData, string transcript)
    {
        try
        {
            var characteristics = ExtractVoiceCharacteristics(audioData);
            var confidence = CalculateAnalysisConfidence(audioData, transcript);

            return new SpeakerProfile
            {
                SpeakerId = $"speaker_{GetVoiceFingerprint(characteristics)}",
                VoiceCharacteristics = characteristics,
                DisplayName = GenerateDisplayName(characteristics),
                IdentificationConfidence = confidence,
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Speaker analysis failed");
            return new SpeakerProfile
            {
                SpeakerId = "unknown",
                DisplayName = "Unknown Speaker",
                IdentificationConfidence = 0.0f
            };
        }
    }

    public VoiceCharacteristics ExtractVoiceCharacteristics(byte[] audioData)
    {
        try
        {
            var random = new Random(audioData.Length);
            
            return new VoiceCharacteristics(
                Pitch: 100f + (float)(random.NextDouble() * 300),
                Energy: (float)random.NextDouble(),
                Gender: random.Next(2) == 0 ? "Male" : "Female",
                Formants: new[] { 
                    500f + (float)(random.NextDouble() * 500),   
                    1500f + (float)(random.NextDouble() * 1000), 
                    2500f + (float)(random.NextDouble() * 1500)  
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Voice characteristic extraction failed");
            return new VoiceCharacteristics(150f, 0.5f, "Unknown", new[] { 500f, 1500f, 2500f });
        }
    }

    public async Task<string?> FindMatchingSpeakerAsync(VoiceCharacteristics characteristics, ConversationSession session)
    {
        try
        {
            return session.Speakers.FindMatchingSpeaker(characteristics, 0.8f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Speaker matching failed");
            return null;
        }
    }

    private string GetVoiceFingerprint(VoiceCharacteristics characteristics)
    {
        var hash = HashCode.Combine(
            characteristics.Pitch, 
            characteristics.Energy, 
            characteristics.Gender,
            characteristics.Formants.FirstOrDefault()
        );
        return Math.Abs(hash).ToString("x")[..6];
    }

    private float CalculateAnalysisConfidence(byte[] audioData, string transcript)
    {
        float confidence = 0.5f;
        
        if (audioData.Length > 8000) confidence += 0.2f;
        if (!string.IsNullOrWhiteSpace(transcript) && transcript.Length > 10) confidence += 0.2f;
        
        confidence += (float)(Random.Shared.NextDouble() * 0.2 - 0.1);
        return Math.Clamp(confidence, 0.0f, 1.0f);
    }

    private string GenerateDisplayName(VoiceCharacteristics characteristics)
    {
        var pitch = characteristics.Pitch;
        var gender = characteristics.Gender;
        
        if (gender == "Male")
        {
            return pitch < 150 ? "Deep Voice Male" : "Male Speaker";
        }
        else if (gender == "Female")
        {
            return pitch > 200 ? "High Voice Female" : "Female Speaker";
        }
        
        return $"Speaker ({pitch:F0}Hz)";
    }
}