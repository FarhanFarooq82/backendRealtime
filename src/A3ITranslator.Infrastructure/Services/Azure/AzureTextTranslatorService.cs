using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Application.Models;
using A3ITranslator.Infrastructure.Configuration;

namespace A3ITranslator.Infrastructure.Services.Azure;

public class AzureTextTranslatorService : IAzureTextTranslatorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureTextTranslatorService> _logger;
    private readonly string _endpoint;
    private readonly string _subscriptionKey;
    private readonly string _region;

    public AzureTextTranslatorService(
        HttpClient httpClient,
        IOptions<ServiceOptions> options,
        ILogger<AzureTextTranslatorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        var azure = options.Value.Azure;
        _subscriptionKey = !string.IsNullOrEmpty(azure.TranslatorKey) ? azure.TranslatorKey : azure.SpeechKey;
        _region = !string.IsNullOrEmpty(azure.TranslatorRegion) ? azure.TranslatorRegion : azure.SpeechRegion;
        
        // Use global endpoint by default or custom if provided
        _endpoint = "https://api.cognitive.microsofttranslator.com/";
    }

    public async Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        try
        {
            var route = $"/translate?api-version=3.0&from={fromLanguage}&to={toLanguage}";
            var url = new Uri(new Uri(_endpoint), route);

            var body = new object[] { new { Text = text } };
            var requestBody = JsonSerializer.Serialize(body);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            
            request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", _region);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var translatedText = result[0].GetProperty("translations")[0].GetProperty("text").GetString();

            return translatedText ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Text Translation failed for '{Text}' from {From} to {To}", text, fromLanguage, toLanguage);
            return text; // Fallback to original text on failure
        }
    }
}
