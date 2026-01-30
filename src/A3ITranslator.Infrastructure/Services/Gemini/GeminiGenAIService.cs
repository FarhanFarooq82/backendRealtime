using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Gemini;

/// <summary>
/// Google Gemini GenAI service - handles structured translation prompts with JSON responses
/// Following official v1beta/v1 REST API documentation: https://ai.google.dev/api/generate-content
/// </summary>
public class GeminiGenAIService : IGenAIService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<GeminiGenAIService> _logger;
    private readonly HttpClient _httpClient;

    public GeminiGenAIService(IOptions<ServiceOptions> options, ILogger<GeminiGenAIService> logger, System.Net.Http.IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("GeminiClient");
    }

    public string GetServiceName() => "Google Gemini";

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var hasConfig = !string.IsNullOrEmpty(_options.Gemini?.ApiKey) && 
                           !string.IsNullOrEmpty(_options.Gemini?.Model);
            _logger.LogDebug("Gemini GenAI health check: {Status}", hasConfig ? "Healthy" : "Unhealthy");
            return await Task.FromResult(hasConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini GenAI health check failed");
            return false;
        }
    }

    public async Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.Gemini?.ApiKey))
            {
                _logger.LogError("Gemini API key not configured");
                throw new InvalidOperationException("Gemini API key not configured");
            }

            if (string.IsNullOrEmpty(_options.Gemini?.Model))
            {
                _logger.LogError("Gemini model not configured");
                throw new InvalidOperationException("Gemini model not configured");
            }

            _logger.LogDebug("Making Gemini API request to model: {Model}", _options.Gemini.Model);

            // Official Gemini 1.5/2.0 request structure
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userPrompt } }
                    }
                },
                systemInstruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    topP = 0.9,
                    maxOutputTokens = 2048,
                    responseMimeType = "application/json" // Request JSON response
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Gemini API endpoint format: https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}
            var endpoint = $"{_options.Gemini.BaseUrl}/models/{_options.Gemini.Model}:generateContent?key={_options.Gemini.ApiKey}";

            var response = await _httpClient.PostAsync(endpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                throw new HttpRequestException($"Gemini API request failed: {response.StatusCode} - {responseContent}");
            }

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent, jsonOptions);

            if (geminiResponse?.Candidates?.Any() == true)
            {
                var candidate = geminiResponse.Candidates[0];
                if (candidate.Content?.Parts?.Any() == true)
                {
                    var textContent = candidate.Content.Parts[0].Text ?? string.Empty;
                    var inputTokens = geminiResponse.UsageMetadata?.PromptTokenCount ?? 0;
                    var outputTokens = geminiResponse.UsageMetadata?.CandidatesTokenCount ?? 0;

                    _logger.LogDebug("Gemini response received, length: {Length}, Usage: In={InputTokens}, Out={OutputTokens}", 
                        textContent.Length, inputTokens, outputTokens);

                    return new GenAIResponse
                    {
                        Content = textContent,
                        Model = _options.Gemini.Model,
                        Usage = new GenAIUsage
                        {
                            InputTokens = inputTokens,
                            OutputTokens = outputTokens
                        }
                    };
                }
                else
                {
                    _logger.LogWarning("Gemini candidate has no content parts. FinishReason: {Reason}", candidate.FinishReason);
                }
            }

            _logger.LogWarning("Gemini returned empty response. Raw format: {Content}", responseContent);
            return new GenAIResponse { Model = _options.Gemini.Model };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stream response tokens from Gemini using Server-Sent Events (SSE)
    /// </summary>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string systemPrompt, 
        string userPrompt, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Gemini?.ApiKey))
        {
            _logger.LogError("Gemini API key not configured");
            throw new InvalidOperationException("Gemini API key not configured");
        }

        _logger.LogDebug("Starting Gemini streaming for model: {Model}", _options.Gemini.Model);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            generationConfig = new
            {
                temperature = 0.3,
                topP = 0.9,
                maxOutputTokens = 2048,
                responseMimeType = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Streaming endpoint
        var endpoint = $"{_options.Gemini.BaseUrl}/models/{_options.Gemini.Model}:streamGenerateContent?key={_options.Gemini.ApiKey}&alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Gemini streaming error: {StatusCode} - {Content}", response.StatusCode, errorContent);
            throw new HttpRequestException($"Gemini streaming failed: {response.StatusCode}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6); // Remove "data: " prefix
            if (data == "[DONE]")
                break;

            // Parse outside try-catch to avoid yield in try-catch issue
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            GeminiResponse? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<GeminiResponse>(data, jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming chunk: {Data}", data);
                continue;
            }

            var text = chunk?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            
            if (!string.IsNullOrEmpty(text))
            {
                _logger.LogTrace("Streaming token: {Token}", text);
                yield return text;
            }
        }
    }

    #region Response Models

    private class GeminiResponse
    {
        public List<Candidate>? Candidates { get; set; }
        public UsageMetadata? UsageMetadata { get; set; }
    }

    private class Candidate
    {
        public Content? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private class Content
    {
        public List<Part>? Parts { get; set; }
        public string? Role { get; set; }
    }

    private class Part
    {
        public string? Text { get; set; }
    }

    private class UsageMetadata
    {
        public int PromptTokenCount { get; set; }
        public int CandidatesTokenCount { get; set; }
        public int TotalTokenCount { get; set; }
    }

    #endregion
}
