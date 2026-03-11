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



    public Task<(string systemPrompt, string userPrompt)> BuildAgent2PromptsAsync(EnhancedTranslationRequest request)
    {
        return _promptService.BuildAgent2PromptsAsync(request);
    }

    public Task<(string systemPrompt, string userPrompt)> BuildFastIntentPromptsAsync(string transcription)
    {
        return _promptService.BuildFastIntentPromptsAsync(transcription);
    }

    public Task<(string systemPrompt, string userPrompt)> BuildAgent3PromptsAsync(EnhancedTranslationRequest request)
    {
        return _promptService.BuildAgent3PromptsAsync(request);
    }

}
