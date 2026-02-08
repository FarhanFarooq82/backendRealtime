using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Azure;

/// <summary>
/// Implements voice assignment logic for speakers using Azure Neural voices.
/// Maintains speaker-to-voice mappings across the session for consistency.
/// Ensures variety when multiple speakers share similar characteristics.
/// </summary>
public class AzureSpeakerVoiceAssignmentService : ISpeakerVoiceAssignmentService
{
    private readonly ILogger<AzureSpeakerVoiceAssignmentService> _logger;
    
    // In-memory cache of speaker voice assignments (per session)
    private readonly Dictionary<string, string> _speakerToVoiceMap = new();
    
    // Voice pools organized by language and gender
    private static readonly Dictionary<string, List<string>> VoicePools = new()
    {
        // English (US)
        ["en-US-Male"] = new() { "en-US-GuyNeural", "en-US-DavisNeural", "en-US-TonyNeural", "en-US-JasonNeural" },
        ["en-US-Female"] = new() { "en-US-JennyNeural", "en-US-AriaNeural", "en-US-SaraNeural", "en-US-NancyNeural" },
        ["en-US-Unknown"] = new() { "en-US-JennyNeural", "en-US-GuyNeural" },
        
        // Danish (Denmark)
        ["da-DK-Male"] = new() { "da-DK-JeppeNeural", "da-DK-ChristofferNeural" },
        ["da-DK-Female"] = new() { "da-DK-ChristelNeural", "da-DK-SofieNeural" },
        ["da-DK-Unknown"] = new() { "da-DK-ChristelNeural", "da-DK-JeppeNeural" },
        
        // Urdu (Pakistan)
        ["ur-PK-Male"] = new() { "ur-PK-AsadNeural", "ur-PK-SaleemNeural" },
        ["ur-PK-Female"] = new() { "ur-PK-UzmaNeural", "ur-PK-GulNeural" },
        ["ur-PK-Unknown"] = new() { "ur-PK-UzmaNeural", "ur-PK-AsadNeural" },
        
        // Fallback defaults per language
        ["en-US"] = new() { "en-US-JennyNeural" },
        ["da-DK"] = new() { "da-DK-ChristelNeural" },
        ["ur-PK"] = new() { "ur-PK-UzmaNeural" }
    };

    public AzureSpeakerVoiceAssignmentService(ILogger<AzureSpeakerVoiceAssignmentService> logger)
    {
        _logger = logger;
    }

    public string AssignVoiceToSpeaker(
        string speakerId, 
        string targetLanguage, 
        string? gender = null,
        Dictionary<string, string>? existingSpeakerVoices = null)
    {
        // Build composite key for speaker + language to ensure appropriate voice per language
        var assignmentKey = $"{speakerId}:{targetLanguage}";

        // Return existing assignment if already mapped for this specific language
        if (_speakerToVoiceMap.TryGetValue(assignmentKey, out var existingVoice))
        {
            _logger.LogDebug("🎤 Speaker {SpeakerId} already assigned voice for {Language}: {Voice}", speakerId, targetLanguage, existingVoice);
            return existingVoice;
        }

        // Normalize gender
        var normalizedGender = NormalizeGender(gender);
        
        // Build pool key (language-gender)
        var poolKey = $"{targetLanguage}-{normalizedGender}";
        
        // Try to get voice pool for language+gender
        if (!VoicePools.TryGetValue(poolKey, out var availableVoices))
        {
            // Fallback to language-only pool
            if (!VoicePools.TryGetValue(targetLanguage, out availableVoices))
            {
                _logger.LogWarning("⚠️ No voice pool found for {Language}. Using default fallback.", targetLanguage);
                availableVoices = new List<string> { "en-US-JennyNeural" }; // Ultimate fallback
            }
        }

        // Get voices already assigned to OTHER speakers for THIS language (to avoid duplicates locally)
        // We can check against the language map
        var usedVoices = (existingSpeakerVoices ?? _speakerToVoiceMap)
            .Where(kvp => kvp.Key.StartsWith($"{speakerId}:") == false && kvp.Key.EndsWith($":{targetLanguage}"))
            .Select(kvp => kvp.Value)
            .ToHashSet();

        // Find an unused voice from the pool
        var selectedVoice = availableVoices.FirstOrDefault(v => !usedVoices.Contains(v));
        
        // If all voices are used, cycle back to first (happens with 3+ speakers of same type)
        if (selectedVoice == null)
        {
            selectedVoice = availableVoices.First();
            _logger.LogInformation("🔄 All voices in use for {PoolKey}, reusing: {Voice}", poolKey, selectedVoice);
        }

        // Store the assignment with Language context
        _speakerToVoiceMap[assignmentKey] = selectedVoice;
        
        _logger.LogInformation("✅ Assigned voice {Voice} to {SpeakerId} for {Language} (Gender: {Gender})", 
            selectedVoice, speakerId, targetLanguage, normalizedGender);

        return selectedVoice;
    }

    public string GetOrAssignVoice(string speakerId, string targetLanguage, string? gender = null)
    {
        return AssignVoiceToSpeaker(speakerId, targetLanguage, gender);
    }

    public void ClearAssignment(string speakerId)
    {
        // Find all keys starting with this speakerId
        var keysToRemove = _speakerToVoiceMap.Keys
            .Where(k => k.StartsWith($"{speakerId}:"))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _speakerToVoiceMap.Remove(key);
        }
        
        if (keysToRemove.Any())
        {
            _logger.LogDebug("🧹 Cleared {Count} voice assignments for speaker {SpeakerId}", keysToRemove.Count, speakerId);
        }
    }

    private static string NormalizeGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return "Unknown";

        return gender.ToLowerInvariant() switch
        {
            "male" or "m" or "1" => "Male",
            "female" or "f" or "2" => "Female",
            _ => "Unknown"
        };
    }
}
