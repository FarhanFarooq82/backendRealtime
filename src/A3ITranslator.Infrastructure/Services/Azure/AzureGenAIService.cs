using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using Azure;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Azure;

/// <summary>
/// Azure OpenAI GenAI service - handles structured translation prompts with JSON responses
/// </summary>
public class AzureGenAIService : IGenAIService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<AzureGenAIService> _logger;
    private readonly OpenAIClient? _openAIClient;

    public AzureGenAIService(IOptions<ServiceOptions> options, ILogger<AzureGenAIService> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        // Initialize Azure OpenAI client
        if (!string.IsNullOrEmpty(_options.Azure?.OpenAIEndpoint) && !string.IsNullOrEmpty(_options.Azure?.OpenAIKey))
        {
            _openAIClient = new OpenAIClient(
                new Uri(_options.Azure.OpenAIEndpoint),
                new AzureKeyCredential(_options.Azure.OpenAIKey)
            );
        }
        else
        {
            _logger.LogWarning("Azure OpenAI credentials not configured properly");
        }
    }

    public string GetServiceName() => "Azure OpenAI";

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            // Check if we have Azure OpenAI config
            var hasConfig = !string.IsNullOrEmpty(_options.Azure?.OpenAIKey) && 
                           !string.IsNullOrEmpty(_options.Azure?.OpenAIEndpoint) &&
                           !string.IsNullOrEmpty(_options.Azure?.OpenAIDeploymentName);
            _logger.LogDebug("Azure GenAI health check: {Status}", hasConfig ? "Healthy" : "Unhealthy");
            return await Task.FromResult(hasConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure GenAI health check failed");
            return false;
        }
    }

    public async Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt, bool useGrounding = false)
    {
        try
        {
            if (_openAIClient == null)
            {
                _logger.LogError("Azure OpenAI client not initialized - check configuration");
                throw new InvalidOperationException("Azure OpenAI service not properly configured");
            }

            if (string.IsNullOrEmpty(_options.Azure?.OpenAIDeploymentName))
            {
                _logger.LogError("Azure OpenAI deployment name not configured");
                throw new InvalidOperationException("Azure OpenAI deployment name not configured");
            }

            _logger.LogDebug("Making Azure OpenAI chat completions request to deployment: {DeploymentName}", _options.Azure.OpenAIDeploymentName);

            // Use Chat Completions API for GPT-4 models
            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = _options.Azure.OpenAIDeploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(systemPrompt),
                    new ChatRequestUserMessage(userPrompt)
                },
                Temperature = 0.3f,
                MaxTokens = 2000,
                NucleusSamplingFactor = 0.9f
            };

            var response = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);

            if (response?.Value?.Choices?.Any() == true)
            {
                var content = response.Value.Choices[0].Message.Content;
                var usage = response.Value.Usage;

                _logger.LogDebug("Azure OpenAI response received, length: {Length}, Usage: In={PromptTokens}, Out={CompletionTokens}", 
                    content?.Length ?? 0, usage?.PromptTokens ?? 0, usage?.CompletionTokens ?? 0);

                return new GenAIResponse
                {
                    Content = content ?? string.Empty,
                    Model = _options.Azure.OpenAIDeploymentName,
                    Usage = new GenAIUsage
                    {
                        InputTokens = usage?.PromptTokens ?? 0,
                        OutputTokens = usage?.CompletionTokens ?? 0
                    }
                };
            }

            _logger.LogWarning("Azure OpenAI returned empty response");
            return new GenAIResponse { Model = _options.Azure.OpenAIDeploymentName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure OpenAI service: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stream response tokens from Azure OpenAI using Server-Sent Events (SSE)
    /// </summary>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string systemPrompt, 
        string userPrompt, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_openAIClient == null)
        {
            _logger.LogError("Azure OpenAI client not initialized - check configuration");
            throw new InvalidOperationException("Azure OpenAI service not properly configured");
        }

        if (string.IsNullOrEmpty(_options.Azure?.OpenAIDeploymentName))
        {
            _logger.LogError("Azure OpenAI deployment name not configured");
            throw new InvalidOperationException("Azure OpenAI deployment name not configured");
        }

        _logger.LogDebug("Starting Azure OpenAI streaming chat completions for deployment: {DeploymentName}", 
            _options.Azure.OpenAIDeploymentName);

        var chatCompletionsOptions = new ChatCompletionsOptions()
        {
            DeploymentName = _options.Azure.OpenAIDeploymentName,
            Messages =
            {
                new ChatRequestSystemMessage(systemPrompt),
                new ChatRequestUserMessage(userPrompt)
            },
            Temperature = 0.3f,
            MaxTokens = 2000,
            NucleusSamplingFactor = 0.9f
        };

        StreamingResponse<StreamingChatCompletionsUpdate>? streamingResponse = null;

        try
        {
            // ✅ Get streaming response using Azure OpenAI SDK
            streamingResponse = await _openAIClient.GetChatCompletionsStreamingAsync(chatCompletionsOptions, cancellationToken);

            // ✅ Enumerate over streaming updates according to Azure SDK
            await foreach (var completionUpdate in streamingResponse.EnumerateValues())
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                // ✅ Access content delta according to Azure OpenAI SDK structure
                if (!string.IsNullOrEmpty(completionUpdate.ContentUpdate))
                {
                    _logger.LogTrace("Streaming token: {Token}", completionUpdate.ContentUpdate);
                    yield return completionUpdate.ContentUpdate;
                }

                // ✅ Check for finish reason
                if (completionUpdate.FinishReason.HasValue)
                {
                    _logger.LogDebug("Azure OpenAI streaming completed with reason: {Reason}", completionUpdate.FinishReason.Value);
                    yield break;
                }
            }
        }
        finally
        {
            // ✅ Ensure proper disposal of streaming response
            streamingResponse?.Dispose();
        }
    }
}
