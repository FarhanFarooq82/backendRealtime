using Microsoft.AspNetCore.Mvc;
using A3ITranslator.Application.Services;
using MediatR;

namespace A3ITranslator.API.Controllers;

[ApiController]
[Route("api")]
public class TranslationController : ControllerBase
{
    private readonly IGenAIService _genAIService;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<TranslationController> _logger;
    private readonly IMediator _mediator;

    public TranslationController(
        IGenAIService genAIService,
        IMetricsService metricsService,
        ILogger<TranslationController> logger,
        IMediator mediator)
    {
        _genAIService = genAIService;
        _metricsService = metricsService;
        _logger = logger;
        _mediator = mediator;
    }

    /// <summary>
    /// Translate text using GenAI service
    /// </summary>
    [HttpPost("translate-text")]
    public async Task<ActionResult<object>> TranslateText([FromBody] TranslateTextRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { error = "Text is required" });
            }

            _logger.LogInformation("Translating text: {Text} from {SourceLang} to {TargetLang}", 
                request.Text, request.SourceLanguage, request.TargetLanguage);

            // Build system prompt for translation
            string systemPrompt = $@"You are a professional translator. 
Translate the following text from {request.SourceLanguage ?? "auto-detected language"} to {request.TargetLanguage ?? "English"}.
Provide only the translation, no explanations or additional text.

Original text: {request.Text}";

            // Generate translation using GenAI service
            var genAIResponse = await _genAIService.GenerateResponseAsync(
                systemPrompt, 
                $"Translate: {request.Text}"
            );
            string translatedText = genAIResponse.Content;

            // Log Metrics
            _ = _metricsService.LogMetricsAsync(new UsageMetrics
            {
                Category = ServiceCategory.Translation,
                Provider = _genAIService.GetServiceName(),
                Operation = "DirectTranslation",
                Model = genAIResponse.Model,
                InputUnits = genAIResponse.Usage.InputTokens,
                InputUnitType = "Tokens",
                OutputUnits = genAIResponse.Usage.OutputTokens,
                OutputUnitType = "Tokens",
                UserPrompt = request.Text,
                Response = translatedText,
                CostUSD = (genAIResponse.Usage.InputTokens * 0.0000025) + (genAIResponse.Usage.OutputTokens * 0.000010)
            });

            return Ok(new
            {
                originalText = request.Text,
                translatedText = translatedText.Trim(),
                sourceLanguage = request.SourceLanguage ?? "auto",
                targetLanguage = request.TargetLanguage ?? "en",
                timestamp = DateTime.UtcNow,
                service = "genai"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate text: {Text}", request.Text);
            return StatusCode(500, new { 
                error = "Translation failed", 
                message = ex.Message,
                text = request.Text 
            });
        }
    }

    /// <summary>
    /// Get supported translation languages
    /// </summary>
    [HttpGet("translation/languages")]
    public ActionResult<object> GetTranslationLanguages()
    {
        var languages = new[]
        {
            new { code = "en", name = "English", flag = "🇺🇸" },
            new { code = "es", name = "Spanish", flag = "🇪🇸" },
            new { code = "fr", name = "French", flag = "🇫🇷" },
            new { code = "de", name = "German", flag = "🇩🇪" },
            new { code = "it", name = "Italian", flag = "🇮🇹" },
            new { code = "pt", name = "Portuguese", flag = "🇵🇹" },
            new { code = "ru", name = "Russian", flag = "🇷🇺" },
            new { code = "ja", name = "Japanese", flag = "🇯🇵" },
            new { code = "ko", name = "Korean", flag = "🇰🇷" },
            new { code = "zh", name = "Chinese", flag = "🇨🇳" },
            new { code = "ar", name = "Arabic", flag = "🇸🇦" },
            new { code = "hi", name = "Hindi", flag = "🇮🇳" },
            new { code = "nl", name = "Dutch", flag = "🇳🇱" },
            new { code = "sv", name = "Swedish", flag = "🇸🇪" },
            new { code = "da", name = "Danish", flag = "🇩🇰" },
            new { code = "no", name = "Norwegian", flag = "🇳🇴" },
            new { code = "fi", name = "Finnish", flag = "🇫🇮" }
        };

        return Ok(new
        {
            languages = languages,
            count = languages.Length,
            service = "translation"
        });
    }

    /// <summary>
    /// Health check for translation service
    /// </summary>
    [HttpGet("translate/health")]
    public ActionResult<object> GetTranslationHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "translation",
            timestamp = DateTime.UtcNow,
            endpoints = new[]
            {
                "POST /api/translate-text",
                "GET /api/translation/languages",
                "GET /api/translate/health"
            }
        });
    }
}

public class TranslateTextRequest
{
    public string Text { get; set; } = string.Empty;
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public string? Context { get; set; }
}
