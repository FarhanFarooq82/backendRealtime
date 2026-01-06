using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.DTOs.Speaker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace A3ITranslator.Infrastructure.Services.Translation;

/// <summary>
/// Orchestrates translation processing with enhanced prompt service and GenAI
/// </summary>
public class TranslationOrchestrator : ITranslationOrchestrator
{
    private readonly ITranslationPromptService _promptService;
    private readonly IGenAIService _genAIService;
    private readonly ILogger<TranslationOrchestrator> _logger;

    public TranslationOrchestrator(
        ITranslationPromptService promptService,
        IGenAIService genAIService,
        ILogger<TranslationOrchestrator> logger)
    {
        _promptService = promptService;
        _genAIService = genAIService;
        _logger = logger;
    }

    public async Task<TranslationResponse> ProcessTranslationAsync(EnhancedTranslationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Starting translation processing for: {Text}", request.Text);

            // Build comprehensive prompts using the prompt service
            var (systemPrompt, userPrompt) = _promptService.BuildTranslationPrompts(request);

            _logger.LogDebug("Generated prompts - System: {SystemLength} chars, User: {UserLength} chars",
                systemPrompt.Length, userPrompt.Length);

            // Get response from GenAI service
            var rawResponse = await _genAIService.GenerateResponseAsync(systemPrompt, userPrompt);

            _logger.LogDebug("Received GenAI response: {ResponseLength} chars", rawResponse?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                _logger.LogWarning("GenAI service returned empty response");
                return CreateFallbackResponse(request, "GenAI service returned empty response", stopwatch.Elapsed);
            }

            // Parse the structured JSON response
            var response = await ParseGenAIResponseAsync(rawResponse, request);
            
            // Set processing time
            response.ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds;

            _logger.LogInformation("Translation completed successfully in {ProcessingTime}ms - Intent: {Intent}, Confidence: {Confidence}",
                stopwatch.Elapsed.TotalMilliseconds, response.Intent, response.Confidence);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process translation for: {Text}", request.Text);
            return CreateFallbackResponse(request, $"Translation processing failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private Task<TranslationResponse> ParseGenAIResponseAsync(string rawResponse, EnhancedTranslationRequest request)
    {
        try
        {
            // Clean the response - remove any markdown code blocks or extra formatting
            var cleanJson = CleanJsonResponse(rawResponse);
            
            _logger.LogDebug("Parsing GenAI response: Original length: {OriginalLength}, Cleaned: {CleanedLength}", 
                rawResponse?.Length ?? 0, cleanJson.Length);
            _logger.LogDebug("Cleaned JSON response: {Json}", cleanJson);

            // Check if response is likely plain text (not JSON)
            if (!cleanJson.TrimStart().StartsWith("{") && !cleanJson.TrimStart().StartsWith("["))
            {
                _logger.LogWarning("GenAI returned plain text instead of JSON. Creating simple response from text: {Text}", cleanJson);
                
                // Handle plain text response - create a simple translation response
                return Task.FromResult(new TranslationResponse
                {
                    Success = true,
                    ProviderUsed = _genAIService.GetServiceName(),
                    ImprovedTranscription = request.Text,
                    Translation = cleanJson.Trim(), // Use the plain text as translation
                    TranslationWithGestures = cleanJson.Trim(),
                    Intent = "SIMPLE_TRANSLATION",
                    AIAssistanceConfirmed = false,
                    Confidence = 0.7f, // Medium confidence for plain text
                    AudioLanguage = request.SourceLanguage,
                    TranslationLanguage = request.TargetLanguage,
                    Reasoning = "Plain text response from GenAI service",
                    ProcessingTimeMs = 0
                });
            }

            // Parse the JSON response using JsonDocument for better error handling
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(cleanJson);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON parsing failed for response: {Response}", cleanJson);
                
                // Fallback: treat entire response as translation text
                return Task.FromResult(new TranslationResponse
                {
                    Success = true,
                    ProviderUsed = _genAIService.GetServiceName(),
                    ImprovedTranscription = request.Text,
                    Translation = rawResponse?.Trim() ?? request.Text, // Use raw response as fallback
                    TranslationWithGestures = rawResponse?.Trim() ?? request.Text,
                    Intent = "SIMPLE_TRANSLATION",
                    AIAssistanceConfirmed = false,
                    Confidence = 0.5f, // Lower confidence for fallback
                    AudioLanguage = request.SourceLanguage,
                    TranslationLanguage = request.TargetLanguage,
                    ErrorMessage = $"JSON parse failed, using raw response: {jsonEx.Message}",
                    Reasoning = "Fallback due to JSON parsing error",
                    ProcessingTimeMs = 0
                });
            }

            using (document)
            {
                var root = document.RootElement;

                var response = new TranslationResponse
                {
                    Success = true,
                    ProviderUsed = _genAIService.GetServiceName(),
                    ImprovedTranscription = GetStringProperty(root, "improvedTranscription") ?? request.Text,
                    Translation = GetStringProperty(root, "translation") ?? request.Text,
                    TranslationWithGestures = GetStringProperty(root, "translationWithGestures") ?? 
                                            GetStringProperty(root, "translation") ?? request.Text,
                    Intent = GetStringProperty(root, "intent") ?? "SIMPLE_TRANSLATION",
                    AIAssistanceConfirmed = GetBoolProperty(root, "aiAssistanceConfirmed"),
                    AIResponse = GetStringProperty(root, "aiResponse"),
                    AIResponseTranslated = GetStringProperty(root, "aiResponseTranslated"),
                    Confidence = GetFloatProperty(root, "confidence") ?? 0.8f,
                    AudioLanguage = GetStringProperty(root, "audioLanguage") ?? request.SourceLanguage,
                    TranslationLanguage = request.TargetLanguage,
                    Reasoning = GetStringProperty(root, "reasoning")
                };

                // Parse gender analysis
                if (root.TryGetProperty("genderAnalysis", out var genderElement))
                {
                    response.SpeakerGender = GetStringProperty(genderElement, "detectedGender") ?? "unknown";
                    response.GenderConfidence = GetFloatProperty(genderElement, "confidence") ?? 0.5f;
                    response.GenderSource = GetStringProperty(genderElement, "source");
                    response.GenderEvidence = GetStringProperty(genderElement, "evidence");
                    response.GenderMismatch = GetBoolProperty(genderElement, "genderMismatch");
                }

                // Parse speaker self-introduction
                if (root.TryGetProperty("speakerSelfIntroduction", out var speakerIntroElement))
                {
                    var detected = GetBoolProperty(speakerIntroElement, "detected");
                    if (detected)
                    {
                        response.SpeakerSelfIntroduction = new SpeakerSelfIntroduction
                        {
                            Detected = true,
                            SpeakerName = GetStringProperty(speakerIntroElement, "name"),
                            Confidence = GetFloatProperty(speakerIntroElement, "confidence") ?? 0.0f,
                            Context = GetStringProperty(speakerIntroElement, "title") ?? GetStringProperty(speakerIntroElement, "organization")
                        };
                    }
                }

                // Parse fact extraction
                if (root.TryGetProperty("factExtraction", out var factElement))
                {
                    response.FactExtraction = new FactExtractionData
                    {
                        RequiresFactExtraction = GetBoolProperty(factElement, "requiresFactExtraction"),
                        Confidence = GetFloatProperty(factElement, "confidence") ?? 0.0f,
                        Facts = new List<ExtractedFact>()
                    };

                    if (factElement.TryGetProperty("facts", out var factsArray) && factsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var factElement2 in factsArray.EnumerateArray())
                        {
                            var fact = new ExtractedFact
                            {
                                Text = GetStringProperty(factElement2, "text") ?? "",
                                EnglishTranslation = GetStringProperty(factElement2, "englishTranslation") ?? "",
                                Type = GetStringProperty(factElement2, "type") ?? "",
                                Category = GetStringProperty(factElement2, "category") ?? "",
                                Confidence = GetFloatProperty(factElement2, "confidence") ?? 0.0f,
                                IsVerified = GetBoolProperty(factElement2, "isVerified"),
                                Context = GetStringProperty(factElement2, "context")
                            };

                            response.FactExtraction.Facts.Add(fact);
                        }
                    }
                }

                // Parse speaker analysis
                if (root.TryGetProperty("speakerAnalysis", out var speakerAnalysisElement))
                {
                    response.SpeakerAnalysis = new SpeakerAnalysisData
                    {
                        SpeakerGender = GetStringProperty(speakerAnalysisElement, "speakerGender") ?? "unknown",
                        DetectedName = GetStringProperty(speakerAnalysisElement, "detectedName"),
                        NameDetected = GetBoolProperty(speakerAnalysisElement, "nameDetected"),
                        Confidence = GetFloatProperty(speakerAnalysisElement, "confidence") ?? 0.0f,
                        Reasoning = GetStringProperty(speakerAnalysisElement, "reasoning"),
                        GenderSource = GetStringProperty(speakerAnalysisElement, "genderSource")
                    };
                }

                _logger.LogDebug("Successfully parsed GenAI JSON response with intent: {Intent}, confidence: {Confidence}",
                    response.Intent, response.Confidence);

                return Task.FromResult(response);
            }
        }
        catch (JsonException jex)
        {
            _logger.LogError(jex, "Failed to parse GenAI JSON response: {Response}", rawResponse);
            return Task.FromResult(CreateFallbackResponse(request, $"Invalid JSON response: {jex.Message}", TimeSpan.Zero));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing GenAI response: {Response}. Exception type: {ExceptionType}", 
                rawResponse, ex.GetType().Name);
            return Task.FromResult(CreateFallbackResponse(request, $"Response parsing error: {ex.Message}", TimeSpan.Zero));
        }
    }

    private string CleanJsonResponse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return "{}";

        var cleaned = rawResponse.Trim();
        
        // Remove markdown code blocks
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7);
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3);
        }
        
        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }
        
        // Remove leading/trailing whitespace
        cleaned = cleaned.Trim();
        
        // Handle cases where the response might be wrapped in quotes
        if (cleaned.StartsWith("\"") && cleaned.EndsWith("\"") && cleaned.Length > 2)
        {
            try
            {
                // Try to unescape if it's a JSON string containing escaped JSON
                var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(cleaned);
                if (!string.IsNullOrEmpty(unescaped) && (unescaped.TrimStart().StartsWith("{") || unescaped.TrimStart().StartsWith("[")))
                {
                    cleaned = unescaped;
                }
            }
            catch
            {
                // If unescaping fails, continue with original
            }
        }
        
        // If still empty after cleaning, return empty JSON object
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "{}";
        }
        
        return cleaned;
    }

    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        try
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                // Handle different JSON value types
                switch (prop.ValueKind)
                {
                    case JsonValueKind.String:
                        var value = prop.GetString();
                        return string.IsNullOrEmpty(value) ? null : value;
                    
                    case JsonValueKind.Number:
                        return prop.GetDecimal().ToString();
                        
                    case JsonValueKind.True:
                        return "true";
                        
                    case JsonValueKind.False:
                        return "false";
                        
                    case JsonValueKind.Null:
                        return null;
                        
                    default:
                        _logger.LogWarning("Property {PropertyName} has unsupported ValueKind: {ValueKind}", 
                            propertyName, prop.ValueKind);
                        return prop.GetRawText(); // Return raw JSON as fallback
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting string property {PropertyName}", propertyName);
            return null;
        }
    }

    private float? GetFloatProperty(JsonElement element, string propertyName)
    {
        try
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                switch (prop.ValueKind)
                {
                    case JsonValueKind.Number:
                        return prop.GetSingle();
                        
                    case JsonValueKind.String:
                        var stringValue = prop.GetString();
                        if (!string.IsNullOrEmpty(stringValue) && float.TryParse(stringValue, out var parsed))
                        {
                            return parsed;
                        }
                        break;
                        
                    default:
                        _logger.LogWarning("Property {PropertyName} has unsupported ValueKind for float: {ValueKind}", 
                            propertyName, prop.ValueKind);
                        break;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting float property {PropertyName}", propertyName);
            return null;
        }
    }

    private bool GetBoolProperty(JsonElement element, string propertyName)
    {
        try
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                switch (prop.ValueKind)
                {
                    case JsonValueKind.True:
                        return true;
                        
                    case JsonValueKind.False:
                        return false;
                        
                    case JsonValueKind.String:
                        var stringValue = prop.GetString()?.ToLowerInvariant();
                        return stringValue == "true" || stringValue == "1" || stringValue == "yes";
                        
                    case JsonValueKind.Number:
                        return prop.GetDecimal() != 0;
                        
                    default:
                        _logger.LogWarning("Property {PropertyName} has unsupported ValueKind for bool: {ValueKind}", 
                            propertyName, prop.ValueKind);
                        break;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bool property {PropertyName}", propertyName);
            return false;
        }
    }

    private TranslationResponse CreateFallbackResponse(EnhancedTranslationRequest request, string errorMessage, TimeSpan processingTime)
    {
        _logger.LogWarning("Creating fallback response: {Error}", errorMessage);

        return new TranslationResponse
        {
            Success = false,
            ImprovedTranscription = request.Text,
            Translation = request.Text, // Fallback to original
            TranslationWithGestures = request.Text,
            Intent = "SIMPLE_TRANSLATION",
            AIAssistanceConfirmed = false,
            Confidence = 0.1f, // Low confidence for fallback
            AudioLanguage = request.SourceLanguage ?? "unknown",
            TranslationLanguage = request.TargetLanguage,
            ProviderUsed = "fallback",
            ErrorMessage = errorMessage,
            ErrorType = TranslationErrorType.ServiceUnavailable,
            ProcessingTimeMs = processingTime.TotalMilliseconds,
            Reasoning = "Fallback response due to processing error"
        };
    }
}
