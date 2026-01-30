using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

/// <summary>
/// Orchestrates GenAI service calls with provider priority and automatic failover
/// Tries providers in configured order until one succeeds
/// </summary>
public class GenAIOrchestrator : IGenAIService
{
    private readonly ILogger<GenAIOrchestrator> _logger;
    private readonly ServiceOptions _options;
    private readonly Dictionary<string, IGenAIService> _providers;

    public GenAIOrchestrator(
        ILogger<GenAIOrchestrator> logger,
        IOptions<ServiceOptions> options,
        IEnumerable<IGenAIService> genAIServices)
    {
        _logger = logger;
        _options = options.Value;
        
        // Build provider dictionary (excluding the orchestrator itself to avoid circular dependency)
        _providers = genAIServices
            .Where(s => s.GetType() != typeof(GenAIOrchestrator))
            .ToDictionary(s => s.GetServiceName(), s => s);

        _logger.LogInformation("GenAI Orchestrator initialized with {Count} providers: {Providers}", 
            _providers.Count, string.Join(", ", _providers.Keys));
    }

    public string GetServiceName() => "GenAI Orchestrator";

    public async Task<bool> CheckHealthAsync()
    {
        // Check if at least one provider is healthy
        foreach (var provider in _providers.Values)
        {
            if (await provider.CheckHealthAsync())
                return true;
        }
        return false;
    }

    public async Task<GenAIResponse> GenerateResponseAsync(string systemPrompt, string userPrompt)
    {
        var providerPriority = (_options.GenAIProviderPriority ?? new[] { "Gemini", "Azure", "OpenAI" }).Distinct();
        var attempts = new List<(string Provider, string Error)>();

        _logger.LogDebug("🎯 GenAI Orchestrator: Trying providers in order: {Priority}", string.Join(" → ", providerPriority));

        foreach (var providerName in providerPriority)
        {
            // Find provider by name (case-insensitive match)
            var provider = _providers.Values.FirstOrDefault(p => 
                p.GetServiceName().Contains(providerName, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                _logger.LogDebug("⚠️ Provider '{Provider}' not found in available services", providerName);
                continue;
            }

            try
            {
                _logger.LogInformation("🚀 Attempting GenAI request with: {Provider}", provider.GetServiceName());
                
                var response = await provider.GenerateResponseAsync(systemPrompt, userPrompt);
                
                if (!string.IsNullOrEmpty(response.Content))
                {
                    _logger.LogInformation("✅ GenAI request succeeded with: {Provider}", provider.GetServiceName());
                    return response;
                }
                
                _logger.LogWarning("⚠️ {Provider} returned empty response, trying next provider", provider.GetServiceName());
                attempts.Add((provider.GetServiceName(), "Empty response"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ {Provider} failed: {Message}. Trying next provider...", 
                    provider.GetServiceName(), ex.Message);
                attempts.Add((provider.GetServiceName(), ex.Message));
            }
        }

        // All providers failed
        var errorMessage = $"All GenAI providers failed. Attempts: {string.Join("; ", attempts.Select(a => $"{a.Provider}: {a.Error}"))}";
        _logger.LogError("❌ {Message}", errorMessage);
        throw new InvalidOperationException(errorMessage);
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string systemPrompt, 
        string userPrompt, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var providerPriority = _options.GenAIProviderPriority ?? new[] { "Gemini", "Azure", "OpenAI" };

        _logger.LogDebug("🎯 GenAI Orchestrator (Streaming): Trying providers in order: {Priority}", 
            string.Join(" → ", providerPriority));

        foreach (var providerName in providerPriority)
        {
            var provider = _providers.Values.FirstOrDefault(p => 
                p.GetServiceName().Contains(providerName, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                _logger.LogDebug("⚠️ Provider '{Provider}' not found in available services", providerName);
                continue;
            }

            bool hasYieldedAnyToken = false;

            // Get enumerator first (can throw), then yield from it
            IAsyncEnumerable<string>? stream = null;
            
            try
            {
                _logger.LogInformation("🚀 Attempting streaming GenAI request with: {Provider}", provider.GetServiceName());
                stream = provider.StreamResponseAsync(systemPrompt, userPrompt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ {Provider} failed to start stream: {Message}. Trying next provider...", 
                    provider.GetServiceName(), ex.Message);
                continue;
            }

            // Now yield from the stream (outside try-catch)
            await foreach (var token in stream)
            {
                hasYieldedAnyToken = true;
                yield return token;
            }

            // If we successfully yielded at least one token, don't try other providers
            if (hasYieldedAnyToken)
            {
                _logger.LogInformation("✅ Streaming GenAI request succeeded with: {Provider}", provider.GetServiceName());
                yield break;
            }

            _logger.LogWarning("⚠️ {Provider} returned empty stream, trying next provider", provider.GetServiceName());
        }

        // All providers failed
        _logger.LogError("❌ All GenAI streaming providers failed");
        throw new InvalidOperationException("All GenAI streaming providers failed");
    }
}
