

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Enhanced translation response that matches the exact JSON structure from our GenAI prompt
/// This is the new structured response that will replace the old TranslationResponse
/// </summary>
public class EnhancedTranslationResponse
{
    // Core translation data - goes to frontend
    public string ImprovedTranscription { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    public string Intent { get; set; } = "SIMPLE_TRANSLATION"; // "SIMPLE_TRANSLATION" or "AI_ASSISTANCE"
    public string TranslationLanguage { get; set; } = string.Empty;
    public string AudioLanguage { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0f;
    public string Reasoning { get; set; } = string.Empty;

    // Speaker identification data - routes to Speaker Service
    public SpeakerIdentificationResult SpeakerIdentification { get; set; } = new();
    public SpeakerProfileUpdateData SpeakerProfileUpdate { get; set; } = new();

    // AI assistance data - routes to AI/Frontend when triggered
    public AIAssistanceData AIAssistance { get; set; } = new();

    // Fact extraction data - routes to Fact Management Service  
    public FactExtractionPayload FactExtraction { get; set; } = new();

    // Processing metadata
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string ProviderUsed { get; set; } = string.Empty;
}

/// <summary>
/// Speaker identification decision from GenAI analysis
/// Routes to: Speaker Management Service
/// </summary>
public class SpeakerIdentificationResult
{
    public string Decision { get; set; } = "UNCERTAIN"; // "CONFIRMED_EXISTING" | "NEW_SPEAKER" | "UNCERTAIN"
    public string? FinalSpeakerId { get; set; }
    public float Confidence { get; set; } = 0f;
    public string Reasoning { get; set; } = string.Empty;
    public List<SpeakerSimilarityScore> SimilarityScores { get; set; } = new();
}

/// <summary>
/// Speaker profile update data from analysis
/// Routes to: Speaker Management Service
/// </summary>
public class SpeakerProfileUpdateData
{
    public string? SpeakerId { get; set; }
    public List<string> NewVocabulary { get; set; } = new();
    public string? Tone { get; set; } // "formal/casual/technical"
    public string? SuggestedName { get; set; }
    public string? EstimatedGender { get; set; } // "male/female/unknown"
    public string? VoiceCharacteristics { get; set; }
    public string? CommunicationStyle { get; set; }
    public List<string> TypicalPhrases { get; set; } = new();
    public string? LanguageComplexity { get; set; }
    public string? TurnContext { get; set; }
    public string? PreferredLanguage { get; set; } // "en-US", "es-ES" etc.
    public float ProfileConfidence { get; set; } = 0f;
}

/// <summary>
/// AI assistance response data
/// Routes to: Frontend (when triggered), TTS Service
/// </summary>
public class AIAssistanceData
{
    public bool TriggerDetected { get; set; } = false;
    public string? Response { get; set; }
    public string? ResponseTranslated { get; set; }
    public string? ResponseLanguage { get; set; }
}

/// <summary>
/// Fact extraction results
/// Routes to: Fact Management Service
/// </summary>
public class FactExtractionPayload
{
    public bool RequiresFactExtraction { get; set; } = false;
    public List<string> Facts { get; set; } = new();
    public float Confidence { get; set; } = 0f;
}

/// <summary>
/// Speaker similarity scoring for identification
/// </summary>
public class SpeakerSimilarityScore
{
    public string SpeakerId { get; set; } = string.Empty;
    public float Score { get; set; } = 0f;
}

/// <summary>
/// Frontend notification payload - only what the UI needs
/// Routes to: Frontend via SignalR
/// </summary>
public class FrontendTranslationNotification
{
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0f;
    public string Intent { get; set; } = "SIMPLE_TRANSLATION";
    public string? AIResponse { get; set; }
    public string? AIResponseTranslated { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// TTS service payload - only what TTS needs
/// Routes to: TTS Service
/// </summary>
public class TTSServicePayload
{
    public string Text { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SpeakerId { get; set; } = string.Empty;
    public string? PreferredVoice { get; set; }
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>
/// Speaker service payload - all speaker-related data
/// Routes to: Speaker Management Service
/// </summary>
public class SpeakerServicePayload
{
    public string SessionId { get; set; } = string.Empty;
    public SpeakerIdentificationResult Identification { get; set; } = new();
    public SpeakerProfileUpdateData ProfileUpdate { get; set; } = new();
    public string AudioLanguage { get; set; } = string.Empty;
    public float TranscriptionConfidence { get; set; } = 0f;
    public A3ITranslator.Application.Models.Speaker.AudioFingerprint? AudioFingerprint { get; set; }
}

/// <summary>
/// Fact management service payload
/// Routes to: Fact Management Service  
/// </summary>
public class FactServicePayload
{
    public string SessionId { get; set; } = string.Empty;
    public string? SpeakerId { get; set; }
    public FactExtractionPayload FactExtraction { get; set; } = new();
    public string OriginalText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
}
