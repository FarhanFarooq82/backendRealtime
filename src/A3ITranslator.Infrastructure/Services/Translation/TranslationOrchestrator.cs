using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Translation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace A3ITranslator.Infrastructure.Services.Translation;

/// <summary>
/// Orchestrates translation processing with Neural Roster management and GenAI.
/// Responsibility: Prompt building, AI inference, result parsing, and metrics logging.
/// </summary>
public class TranslationOrchestrator : ITranslationOrchestrator
{
    private readonly ITranslationPromptService _promptService;
    private readonly IGenAIService _genAIService;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<TranslationOrchestrator> _logger;

    public TranslationOrchestrator(
        ITranslationPromptService promptService,
        IGenAIService genAIService,
        IMetricsService metricsService,
        ILogger<TranslationOrchestrator> logger)
    {
        _promptService = promptService;
        _genAIService = genAIService;
        _metricsService = metricsService;
        _logger = logger;
    }

    public async Task<EnhancedTranslationResponse> ProcessEnhancedTranslationAsync(EnhancedTranslationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        string systemPrompt = string.Empty;
        string userPrompt = string.Empty;
        string? rawResponse = null;
        
        try
        {
            _logger.LogInformation("🚀 Starting NEURAL translation processing for: {Text}", request.Text);

            // 1. Build comprehensive prompts (including Session Context and Facts)
            (systemPrompt, userPrompt) = await _promptService.BuildTranslationPromptsAsync(request);

            // 2. Clear target context if needed (handled in PromptService)
            
            // 3. Get response from GenAI service
            // 🧠 Use Grounding (Search) only for "Brain" track. It handles the intent to use it or not.
            var genAIResponse = await _genAIService.GenerateResponseAsync(systemPrompt, userPrompt, useGrounding: !request.IsPulse);
            rawResponse = genAIResponse.Content;

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                _logger.LogWarning("⚠️ GenAI service returned empty response");
                return CreateEnhancedFallbackResponse(request, "GenAI service returned empty response", stopwatch.Elapsed);
            }

            // 4. Parse the new structured Neural Roster JSON response
            var response = ParseEnhancedGenAIResponse(rawResponse, request);
            
            response.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;
            response.ProviderUsed = _genAIService.GetServiceName();
            response.Usage = genAIResponse.Usage;
            response.TurnId = request.TurnId;
            response.IsPulse = request.IsPulse;

            // 5. ✨ LOG COMPLETE USER PROMPT (Facts included) & RESPONSE (No System Prompt)
            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                SessionId = request.SessionId,
                Category = ServiceCategory.Translation,
                Provider = _genAIService.GetServiceName(),
                Operation = "NeuralTranslation",
                Model = genAIResponse.Model,
                InputUnits = genAIResponse.Usage.InputTokens,
                InputUnitType = "Tokens",
                OutputUnits = genAIResponse.Usage.OutputTokens,
                OutputUnitType = "Tokens",
                UserPrompt = userPrompt, // Complete prompt including facts/history
                Response = rawResponse,
                CostUSD = (genAIResponse.Usage.InputTokens * 0.0000025) + (genAIResponse.Usage.OutputTokens * 0.000010),
                LatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                TurnId = request.TurnId,
                Track = request.IsPulse ? "Pulse" : "Brain"
            });

            _logger.LogInformation("✅ Neural translation completed in {ProcessingTime}ms - Speaker: {SpeakerId}, Action: {Action}",
                stopwatch.Elapsed.TotalMilliseconds, response.TurnAnalysis.ActiveSpeakerId, response.TurnAnalysis.DecisionType);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process neural translation for: {Text}", request.Text);

            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                SessionId = request.SessionId,
                Category = ServiceCategory.Translation,
                Provider = _genAIService.GetServiceName(),
                Operation = "NeuralTranslation",
                Status = "Error",
                ErrorMessage = ex.Message,
                UserPrompt = userPrompt,
                Response = rawResponse ?? "EMPTY",
                LatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                TurnId = request.TurnId,
                Track = request.IsPulse ? "Pulse" : "Brain"
            });

            return CreateEnhancedFallbackResponse(request, $"Neural translation failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private EnhancedTranslationResponse ParseEnhancedGenAIResponse(string rawResponse, EnhancedTranslationRequest request)
    {
        try
        {
            var cleaned = CleanJsonResponse(rawResponse);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<EnhancedTranslationResponse>(cleaned, options) 
                          ?? throw new JsonException("Failed to deserialize response");
            
            response.Success = true;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ JSON parsing failed for raw response: {Raw}", rawResponse);
            return CreateEnhancedFallbackResponse(request, $"JSON Parse Error: {ex.Message}", TimeSpan.Zero);
        }
    }

    private string CleanJsonResponse(string rawResponse)
    {
        var cleaned = rawResponse.Trim();
        if (cleaned.StartsWith("```json")) cleaned = cleaned.Substring(7);
        else if (cleaned.StartsWith("```")) cleaned = cleaned.Substring(3);
        if (cleaned.EndsWith("```")) cleaned = cleaned.Substring(0, cleaned.Length - 3);
        return cleaned.Trim();
    }

    private EnhancedTranslationResponse CreateEnhancedFallbackResponse(EnhancedTranslationRequest request, string errorMessage, TimeSpan processingTime)
    {
        return new EnhancedTranslationResponse
        {
            Success = false,
            ErrorMessage = errorMessage,
            ProcessingTimeMs = processingTime.TotalMilliseconds,
            ImprovedTranscription = request.Text,
            Translation = request.Text,
            TurnAnalysis = new TurnAnalysisData { DecisionType = "UNCERTAIN" }
        };
    }


    public async Task<string> GenerateSummaryInLanguageAsync(string conversationHistory, string language)
    {
        var stopwatch = Stopwatch.StartNew();
        var (systemPrompt, userPrompt) = await _promptService.BuildNativeSummaryPromptsAsync(conversationHistory, language);
        var response = await _genAIService.GenerateResponseAsync(systemPrompt, userPrompt);
        
        _ = _metricsService.LogMetricsAsync(new UsageMetrics
        {
            Category = ServiceCategory.Summarization,
            Provider = _genAIService.GetServiceName(),
            Operation = "NativeLanguageSummary",
            UserPrompt = userPrompt,
            Response = response.Content,
            LatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds
        });

        return response.Content;
    }
}
