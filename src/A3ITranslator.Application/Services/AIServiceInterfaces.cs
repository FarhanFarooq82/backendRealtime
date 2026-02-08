using A3ITranslator.Application.Common;
using A3ITranslator.Application.Models;

namespace A3ITranslator.Application.Services;


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
    public GroundingInfo? GroundingInfo { get; set; }
}

public class GroundingInfo
{
    public string? SearchEntryPoint { get; set; }
    public List<string>? GroundingChunks { get; set; }
    public List<string>? WebSearchQueries { get; set; }
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
    Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt, bool useGrounding = false);
    
    /// <summary>
    /// Stream response tokens from system and user prompts
    /// </summary>
    IAsyncEnumerable<string> StreamResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if the service is healthy and available
    /// </summary>
    Task<bool> CheckHealthAsync();
}

