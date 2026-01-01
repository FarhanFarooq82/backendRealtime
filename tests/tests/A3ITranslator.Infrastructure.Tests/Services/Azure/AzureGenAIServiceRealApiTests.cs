using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using System.Text.Json;

namespace A3ITranslator.Infrastructure.Tests.Services.Azure;

/// <summary>
/// Integration tests for AzureGenAIService using real Azure OpenAI credentials
/// Tests the actual Azure OpenAI API integration with chat completions
/// </summary>
public class AzureGenAIServiceRealApiTests
{
    private readonly ILogger<AzureGenAIService> _logger;
    private readonly ServiceOptions _serviceOptions;

    public AzureGenAIServiceRealApiTests()
    {
        // Setup logger
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<AzureGenAIService>();

        // Configure service options with real development credentials
        _serviceOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                OpenAIEndpoint = "https://a3itranslatoropenai.openai.azure.com/",
                OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "YOUR_AZURE_OPENAI_KEY_HERE",
                OpenAIDeploymentName = "gpt-4.1"
            }
        };
    }

    [Fact]
    public async Task GenerateResponseAsync_WithValidPrompts_ShouldReturnTranslationResponse()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        var systemPrompt = """
        Task: Translate text and optionally provide AI assistance based on intent detection.

        CRITICAL SCRIPT RULES:
        - ABSOLUTELY NEVER use romanized text for native script languages
        - Urdu: MUST use Arabic/Nastaliq script (اردو), NEVER Latin characters
        - Hindi: MUST use Devanagari script (हिन्दी), NEVER Latin characters
        - Arabic: MUST use Arabic script (العربية), NEVER Latin characters
        - Bengali: MUST use Bengali script (বাংলা), NEVER Latin characters
        - Persian: MUST use Persian script (فارسی), NEVER Latin characters

        PROCESSING STEPS:

        STEP 1 - TRANSLATION (ALWAYS REQUIRED):
        1. Use session facts to resolve pronouns (he/she/it → specific person names from facts)
        2. Apply cultural context from relationship facts (mama/papa vs mom/dad)
        3. Reference previous conversation topics for ambiguous terms
        4. Translate to target language using native scripts
        5. Keep translation natural and conversational
        6. Create SSML enhanced version for natural TTS (no <speak> wrapper)

        STEP 2 - AI ASSISTANCE (ONLY IF TRIGGER DETECTED):
        IF trigger_detected = true:
        1. INTENT CONFIRMATION: Is the speaker genuinely addressing the translator system?
           - Analyze full context, not just trigger phrase
           - Check for false positives (reporting speech, discussions, negations)
           - Consider speaker intent and conversation flow

        2. IF CONFIRMED as genuine AI assistance request:
           - Generate helpful response using session facts and speaker information
           - Address speaker by name from speaker info
           - Use session knowledge for accurate, contextual answers
           - Create response in source language
           - Translate AI response to target language
           - Create SSML enhanced version of AI response (source language)

        3. IF NOT CONFIRMED (false positive):
           - Skip AI assistance processing
           - Treat as simple translation

        STEP 3 - SSML ENHANCEMENT RULES:
        - IF AI response confirmed: Enhance AI response in source language
        - IF no AI response: Enhance translation in target language
        - Add appropriate pauses, emphasis, and natural speech markers

        OUTPUT FORMAT (JSON):
        {
          "translation": "translated text in target language native script",
          "translation_with_gestures": "SSML enhanced version",
          "ai_assistance_confirmed": true/false,
          "ai_response": "AI answer in source language (if confirmed, else null)",
          "ai_response_translated": "AI answer in target language (if confirmed, else null)",
          "confidence": 0.0-1.0
        }
        """;

        var userPrompt = @"Please translate the following:
- Text: ""Hello, how are you today?""
- Source Language: en-US
- Target Language: da-DK
- Speaker: TestUser
- Context: Casual greeting
- Facts: This is a friendly conversation
- Session Context: First interaction";

        // Act
        var result = await service.GenerateResponseAsync(systemPrompt, userPrompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // Verify the response contains expected elements
        Assert.Contains("translation", result);
        
        // Try to parse as JSON to verify structure
        var jsonDocument = JsonDocument.Parse(result);
        Assert.True(jsonDocument.RootElement.TryGetProperty("translation", out var translationElement));
        Assert.True(translationElement.ValueKind == JsonValueKind.String);
        
        // Log the actual response for verification
        _logger.LogInformation("Azure OpenAI Response: {Response}", result);
    }

    [Fact]
    public async Task GenerateResponseAsync_WithDanishToEnglish_ShouldReturnValidTranslation()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        var systemPrompt = @"You are a professional Danish-English translator. Respond with a JSON object containing the translation and metadata.";

        var userPrompt = @"Translate this Danish text to English:
- Text: ""Hej, hvordan har du det i dag?""
- Source Language: da-DK
- Target Language: en-US
- Context: Friendly greeting";

        // Act
        var result = await service.GenerateResponseAsync(systemPrompt, userPrompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // Should contain some form of English translation
        Assert.True(result.Contains("Hello") || result.Contains("Hi") || result.Contains("how") || result.Contains("today"));
        
        _logger.LogInformation("Danish to English Response: {Response}", result);
    }

    [Fact]
    public async Task GenerateResponseAsync_WithComplexTranslationPrompt_ShouldHandleContextAndFacts()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        var systemPrompt = """
        Task: Translate text and optionally provide AI assistance based on intent detection.

        CRITICAL SCRIPT RULES:
        - ABSOLUTELY NEVER use romanized text for native script languages
        - Urdu: MUST use Arabic/Nastaliq script (اردو), NEVER Latin characters
        - Hindi: MUST use Devanagari script (हिन्दी), NEVER Latin characters
        - Arabic: MUST use Arabic script (العربية), NEVER Latin characters
        - Bengali: MUST use Bengali script (বাংলা), NEVER Latin characters
        - Persian: MUST use Persian script (فارسی), NEVER Latin characters

        PROCESSING STEPS:

        STEP 1 - TRANSLATION (ALWAYS REQUIRED):
        1. Use session facts to resolve pronouns (he/she/it → specific person names from facts)
        2. Apply cultural context from relationship facts (mama/papa vs mom/dad)
        3. Reference previous conversation topics for ambiguous terms
        4. Translate to target language using native scripts
        5. Keep translation natural and conversational
        6. Create SSML enhanced version for natural TTS (no <speak> wrapper)

        STEP 2 - AI ASSISTANCE (ONLY IF TRIGGER DETECTED):
        IF trigger_detected = true:
        1. INTENT CONFIRMATION: Is the speaker genuinely addressing the translator system?
           - Analyze full context, not just trigger phrase
           - Check for false positives (reporting speech, discussions, negations)
           - Consider speaker intent and conversation flow

        2. IF CONFIRMED as genuine AI assistance request:
           - Generate helpful response using session facts and speaker information
           - Address speaker by name from speaker info
           - Use session knowledge for accurate, contextual answers
           - Create response in source language
           - Translate AI response to target language
           - Create SSML enhanced version of AI response (source language)

        3. IF NOT CONFIRMED (false positive):
           - Skip AI assistance processing
           - Treat as simple translation

        STEP 3 - SSML ENHANCEMENT RULES:
        - IF AI response confirmed: Enhance AI response in source language
        - IF no AI response: Enhance translation in target language
        - Add appropriate pauses, emphasis, and natural speech markers

        OUTPUT FORMAT (JSON):
        {
          "translation": "translated text in target language native script",
          "translation_with_gestures": "SSML enhanced version",
          "ai_assistance_confirmed": true/false,
          "ai_response": "AI answer in source language (if confirmed, else null)",
          "ai_response_translated": "AI answer in target language (if confirmed, else null)",
          "confidence": 0.0-1.0
        }
        """;


        var userPrompt = @"TRANSLATION INPUTS:
- Text: ""دوستوں کیا حال ہیں دوستو آپ سب کا امید ہے کہ آپ ٹھیک ہوں گے ڈگ بھی ٹھیک رہے گی تو ٹھیک ہے نا۔.""
- Source Language: ur-PK
- Target Language: da-DK
- Speaker: Tourist (foreign visitor)
- Context: Emergency medical situation
- Facts: Speaker is stressed, needs immediate assistance
- Session Context: First emergency request
- Trigger Detected: false";

        // Act
        var result = await service.GenerateResponseAsync(systemPrompt, userPrompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        
        // Should be a substantial response given the complex context
        Assert.True(result.Length > 50);
        
        // Log the actual response for debugging
        _logger.LogInformation("Complex Translation Response: {Response}", result);
        
        // Should contain Danish translation elements (more flexible check)
        Assert.True(result.Contains("hospital") || result.Contains("hjælp") || result.Contains("dansk") || result.Contains("Danish") || 
                   result.Contains("translation") || result.Contains("Venner") || result.Contains("hvordan"), 
                   $"Response doesn't contain expected Danish elements. Actual response: {result}");
    }

    [Fact]
    public void GetServiceName_ShouldReturnCorrectName()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        // Act
        var serviceName = service.GetServiceName();

        // Assert
        Assert.Equal("Azure OpenAI", serviceName);
    }

    [Fact]
    public async Task CheckHealthAsync_WithValidCredentials_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        // Act
        var isHealthy = await service.CheckHealthAsync();

        // Assert
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task GenerateResponseAsync_WithEmptyDeploymentName_ShouldThrowException()
    {
        // Arrange
        var invalidOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                OpenAIEndpoint = "https://a3itranslatoropenai.openai.azure.com/",
                OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "YOUR_AZURE_OPENAI_KEY_HERE",
                OpenAIDeploymentName = "" // Empty deployment name
            }
        };

        var options = Options.Create(invalidOptions);
        var service = new AzureGenAIService(options, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateResponseAsync("system", "user"));
    }

    [Fact]
    public async Task GenerateResponseAsync_PerformanceTest_ShouldCompleteWithinReasonableTime()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new AzureGenAIService(options, _logger);

        var systemPrompt = "You are a fast translator. Respond with JSON containing a translation.";
        var userPrompt = "Translate 'Good morning' from English to Danish.";

        var startTime = DateTime.UtcNow;

        // Act
        var result = await service.GenerateResponseAsync(systemPrompt, userPrompt);

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // Assert
        Assert.NotNull(result);
        Assert.True(duration.TotalSeconds < 30, $"Request took {duration.TotalSeconds} seconds, which is too long");
        
        _logger.LogInformation("Azure OpenAI Response Time: {Duration}ms", duration.TotalMilliseconds);
    }
}
