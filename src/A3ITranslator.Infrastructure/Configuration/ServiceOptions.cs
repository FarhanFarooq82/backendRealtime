using System.ComponentModel.DataAnnotations;

namespace A3ITranslator.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure Speech Services
/// Following Azure Speech SDK documentation patterns
/// </summary>
public class AzureOptions
{
    public const string SectionName = "Azure";
    
    [Required]
    public string SpeechKey { get; set; } = string.Empty;
    
    [Required]
    public string SpeechRegion { get; set; } = string.Empty;
    
    public string SpeechEndpoint { get; set; } = string.Empty;

    // Azure OpenAI configuration
    public string OpenAIEndpoint { get; set; } = string.Empty;
    
    public string OpenAIKey { get; set; } = string.Empty;
    
    public string OpenAIDeploymentName { get; set; } = "gpt-4";


    public bool EnableSpeakerRecognition { get; set; } = true;
    
    // Speaker Recognition Settings
    public int MaxSpeakerProfiles { get; set; } = 20; // Maximum speaker profiles per session
    public double SpeakerIdentificationThreshold { get; set; } = 0.65; // Minimum confidence for identification
    public bool EnableSpeakerEnrollment { get; set; } = true; // Allow new speaker enrollment
    public int MinEnrollmentDurationSeconds { get; set; } = 3; // Minimum audio length for enrollment
}

/// <summary>
/// Configuration options for OpenAI services
/// Following OpenAI .NET SDK documentation patterns
/// </summary>
public class OpenAIOptions
{
    public const string SectionName = "OpenAI";
    
    [Required]
    public string ApiKey { get; set; } = string.Empty;
    
    public string Organization { get; set; } = string.Empty;
    
    public string WhisperModel { get; set; } = "whisper-1";
    
    public string ChatModel { get; set; } = "gpt-4o-mini";
    
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// Configuration options for Google Cloud services
/// Following Google Cloud SDK patterns
/// </summary>
public class GoogleOptions
{
    public const string SectionName = "Google";
    
    [Required]
    public string CredentialsPath { get; set; } = string.Empty;
    
    public string ProjectId { get; set; } = string.Empty;
    
    // GDPR-compliant European region - Belgium (confirmed available for STT v2)  
    public string Location { get; set; } = "europe-west4"; // Saint-Ghislain, Belgium - confirmed STT v2 support
    
    // Regional API endpoint (will fallback to global if not available)
    public string ApiEndpoint { get; set; } = "europe-west4-speech.googleapis.com";
    
    // Recognizer ID for the regional recognizer
    public string RecognizerId { get; set; } = "eu4-chirp2-recognizer";
    
    // Speech-to-Text model configuration
    public string STTModel { get; set; } = "chirp_2"; // Default to chirp_2 for better accuracy
}

/// <summary>
/// Configuration options for Gemini AI services
/// Following Google AI SDK patterns
/// </summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";
    
    [Required]
    public string ApiKey { get; set; } = string.Empty;
    
    public string Model { get; set; } = "gemini-1.5-flash";
    
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

/// <summary>
/// Quality thresholds for STT services
/// Used for fallback decision making
/// </summary>
public class STTQualityThresholds
{
    public float MinimumConfidenceScore { get; set; } = 0.7f;
    public float PreferredConfidenceScore { get; set; } = 0.85f;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
}

/// <summary>
/// Overall service configuration with provider priorities
/// Based on architectural requirements in documentation
/// </summary>
public class ServiceOptions
{
    public const string SectionName = "Services";
    
    public AzureOptions Azure { get; set; } = new();
    
    public GoogleOptions Google { get; set; } = new();
    
    public OpenAIOptions OpenAI { get; set; } = new();
    
    public GeminiOptions Gemini { get; set; } = new();
    
    public STTQualityThresholds STTQualityThresholds { get; set; } = new();
    
    /// <summary>
    /// Provider priority for STT services: Azure -> OpenAI -> Google
    /// Based on documented architecture decisions
    /// </summary>
    public string[] STTProviderPriority { get; set; } = { "Azure", "OpenAI" };
    
    /// <summary>
    /// Provider priority for GenAI services: OpenAI -> Gemini -> Azure
    /// Optimized for response quality and cost efficiency
    /// </summary>
    public string[] GenAIProviderPriority { get; set; } = { "Gemini", "Azure", "OpenAI" };
    
    /// <summary>
    /// Provider priority for TTS services: Azure (primary)
    /// Based on documented voice quality requirements
    /// </summary>
    public string[] TTSProviderPriority { get; set; } = { "Azure" };
}