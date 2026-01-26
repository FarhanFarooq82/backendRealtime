using A3ITranslator.Application.Common;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Speech-to-Text service interface
/// </summary>
public interface ISTTService
{
    /// <summary>
    /// Get supported languages for this STT service
    /// </summary>
    Dictionary<string, string> GetSupportedLanguages();
    
    /// <summary>
    /// Get the service name for identification
    /// </summary>
    string GetServiceName();
    
    /// <summary>
    /// Convert speech to text
    /// </summary>
    Task<Result<string>> ConvertSpeechToTextAsync(byte[] audioData, string languageCode, string sessionId);
    
    /// <summary>
    /// Check if the service is healthy and available
    /// </summary>
    Task<bool> CheckHealthAsync();
    
    /// <summary>
    /// Indicates if this service supports language detection
    /// </summary>
    bool SupportsLanguageDetection { get; }
    
    /// <summary>
    /// Indicates if this service requires audio format conversion
    /// </summary>
    bool RequiresAudioConversion { get; }
    
    /// <summary>
    /// Transcribe audio with language detection (fallback-ready method)
    /// </summary>
    Task<DTOs.Audio.STTResult> TranscribeWithDetectionAsync(
        byte[] audio,
        string[] candidateLanguages,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Text-to-Speech service interface
/// </summary>
public interface ITTSService
{
    /// <summary>
    /// Get supported languages for this TTS service
    /// </summary>
    Dictionary<string, string> GetSupportedLanguages();
    
    /// <summary>
    /// Get the service name for identification
    /// </summary>
    string GetServiceName();
    
    /// <summary>
    /// Convert text to speech
    /// </summary>
    Task<Result<byte[]>> ConvertTextToSpeechAsync(string text, string languageCode, string sessionId);
    
    /// <summary>
    /// Check if the service is healthy and available
    /// </summary>
    Task<bool> CheckHealthAsync();
}



public class GenAIUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens => InputTokens + OutputTokens;
}

public class GenAIResponse
{
    public string Content { get; set; } = string.Empty;
    public GenAIUsage Usage { get; set; } = new();
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// GenAI service interface for system/user prompt processing
/// Supports both traditional and streaming responses
/// </summary>
public interface IGenAIService
{
    /// <summary>
    /// Get the service name for identification
    /// </summary>
    string GetServiceName();
    
    /// <summary>
    /// Generate response from system and user prompts
    /// </summary>
    Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt);
    
    /// <summary>
    /// Stream response tokens from system and user prompts
    /// </summary>
    IAsyncEnumerable<string> StreamResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if the service is healthy and available
    /// </summary>
    Task<bool> CheckHealthAsync();
}

