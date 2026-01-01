using A3ITranslator.Application.DTOs.Speaker;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Response from enhanced translation service
/// </summary>
public class TranslationResponse
{
    public bool Success { get; set; } = true;
    public string ImprovedTranscription { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    public string TranslationWithGestures { get; set; } = string.Empty;
    public bool AIAssistanceConfirmed { get; set; }
    public string? AIResponse { get; set; }
    public string? AIResponseTranslated { get; set; }
    public float Confidence { get; set; }
    public string ProviderUsed { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string? AudioLanguage { get; set; }
    public string? TranslationLanguage { get; set; }
    public string? SpeakerAcknowledged { get; set; }
    public SpeakerInfo? SpeakerInfo { get; set; }
    public double ProcessingTimeMs { get; set; }
    
    // Legacy speaker analysis properties (for backward compatibility)
    public string ServiceUsed { get; set; } = string.Empty;
    public string SpeakerGender { get; set; } = "unknown";
    public bool GenderMismatch { get; set; }
    
    // NEW: Enhanced gender detection
    public float GenderConfidence { get; set; } = 0.5f;
    public string? GenderSource { get; set; } // "linguistic", "pronoun", "unknown"
    public string? GenderEvidence { get; set; } // Grammatical markers or pronouns
    
    // NEW: Speaker self-introduction detection
    public SpeakerSelfIntroduction? SpeakerSelfIntroduction { get; set; }
    
    public List<string> BackgroundTasks { get; set; } = new();
    public string Intent { get; set; } = "SIMPLE_TRANSLATION"; // SIMPLE_TRANSLATION or AI_ASSISTANCE
    
    // Fact extraction and speaker analysis from LLM response
    public FactExtractionData? FactExtraction { get; set; }
    public SpeakerAnalysisData? SpeakerAnalysis { get; set; }
    
    public string? ErrorMessage { get; set; }
    public TranslationErrorType? ErrorType { get; set; }
}

/// <summary>
/// Speaker self-introduction detection result
/// </summary>
public class SpeakerSelfIntroduction
{
    public bool Detected { get; set; }
    public string? SpeakerName { get; set; }
    public float Confidence { get; set; }
    public string? Context { get; set; } // The actual phrase used
}

/// <summary>
/// Translation error types for better error handling
/// </summary>
public enum TranslationErrorType
{
    NetworkError,
    AuthenticationError,
    RateLimitExceeded,
    UnsupportedLanguage,
    InvalidRequest,
    AllModelsUnavailable,
    AllProvidersUnavailable,
    TokenLimitExceeded,
    ServiceUnavailable
}
