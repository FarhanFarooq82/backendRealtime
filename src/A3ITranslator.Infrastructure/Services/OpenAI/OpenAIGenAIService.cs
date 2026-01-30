using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.OpenAI;

/// <summary>
/// OpenAI GenAI service - handles structured translation prompts with JSON responses
/// Uses GPT-4o-mini for cost-effective inference
/// </summary>
public class OpenAIGenAIService : IGenAIService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<OpenAIGenAIService> _logger;
    private readonly HttpClient _httpClient;

    public OpenAIGenAIService(IOptions<ServiceOptions> options, ILogger<OpenAIGenAIService> logger, System.Net.Http.IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("OpenAIClient");
    }

    public string GetServiceName() => "OpenAI";

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var hasConfig = !string.IsNullOrEmpty(_options.OpenAI?.ApiKey);
            _logger.LogDebug("OpenAI GenAI health check: {Status}", hasConfig ? "Healthy" : "Unhealthy");
            return await Task.FromResult(hasConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI GenAI health check failed");
            return false;
        }
    }

    public async Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.OpenAI?.ApiKey))
            {
                _logger.LogError("OpenAI API key not configured");
                throw new InvalidOperationException("OpenAI API key not configured");
            }

            _logger.LogDebug("Making OpenAI API request to model: {Model}", _options.OpenAI.ChatModel);

            var requestBody = new
            {
                model = _options.OpenAI.ChatModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 2000,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.OpenAI.BaseUrl}/chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_options.OpenAI.ApiKey}");
            
            if (!string.IsNullOrEmpty(_options.OpenAI.Organization))
            {
                request.Headers.Add("OpenAI-Organization", _options.OpenAI.Organization);
            }

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                throw new HttpRequestException($"OpenAI API request failed: {response.StatusCode} - {responseContent}");
            }

            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent);

            if (openAIResponse?.Choices?.Any() == true)
            {
                var textContent = openAIResponse.Choices[0].Message?.Content ?? string.Empty;
                var inputTokens = openAIResponse.Usage?.PromptTokens ?? 0;
                var outputTokens = openAIResponse.Usage?.CompletionTokens ?? 0;

                _logger.LogDebug("OpenAI response received, length: {Length}, Usage: In={InputTokens}, Out={OutputTokens}", 
                    textContent.Length, inputTokens, outputTokens);

                return new GenAIResponse
                {
                    Content = textContent,
                    Model = _options.OpenAI.ChatModel,
                    Usage = new GenAIUsage
                    {
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens
                    }
                };
            }

            _logger.LogWarning("OpenAI returned empty response");
            return new GenAIResponse { Model = _options.OpenAI.ChatModel };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API: {Message}", ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string systemPrompt, 
        string userPrompt, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.OpenAI?.ApiKey))
        {
            _logger.LogError("OpenAI API key not configured");
            throw new InvalidOperationException("OpenAI API key not configured");
        }

        _logger.LogDebug("Starting OpenAI streaming for model: {Model}", _options.OpenAI.ChatModel);

        var requestBody = new
        {
            model = _options.OpenAI.ChatModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.3,
            max_tokens = 2000,
            stream = true,
            response_format = new { type = "json_object" }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.OpenAI.BaseUrl}/chat/completions")
        {
            Content = content
        };
        request.Headers.Add("Authorization", $"Bearer {_options.OpenAI.ApiKey}");
        
        if (!string.IsNullOrEmpty(_options.OpenAI.Organization))
        {
            request.Headers.Add("OpenAI-Organization", _options.OpenAI.Organization);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI streaming error: {StatusCode} - {Content}", response.StatusCode, errorContent);
            throw new HttpRequestException($"OpenAI streaming failed: {response.StatusCode}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);
            if (data == "[DONE]")
                break;

            // Parse outside try-catch to avoid yield in try-catch issue
            OpenAIStreamChunk? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming chunk: {Data}", data);
                continue;
            }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            
            if (!string.IsNullOrEmpty(delta))
            {
                _logger.LogTrace("Streaming token: {Token}", delta);
                yield return delta;
            }
        }
    }

    #region Response Models

    private class OpenAIResponse
    {
        public List<Choice>? Choices { get; set; }
        public Usage? Usage { get; set; }
    }

    private class Choice
    {
        public Message? Message { get; set; }
    }

    private class Message
    {
        public string? Content { get; set; }
    }

    private class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }
        
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }

    private class OpenAIStreamChunk
    {
        public List<StreamChoice>? Choices { get; set; }
    }

    private class StreamChoice
    {
        public StreamDelta? Delta { get; set; }
    }

    private class StreamDelta
    {
        public string? Content { get; set; }
    }

    #endregion
}
