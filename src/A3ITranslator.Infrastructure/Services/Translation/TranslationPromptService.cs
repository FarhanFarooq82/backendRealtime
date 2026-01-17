using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Translation;
using A3ITranslator.Application.Services.Speaker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace A3ITranslator.Infrastructure.Services.Translation;

/// <summary>
/// Service for building comprehensive translation prompts with enhanced linguistic analysis
/// </summary>
public class TranslationPromptService : ITranslationPromptService
{
    private readonly ILogger<TranslationPromptService> _logger;
    private readonly ISpeakerManagementService _speakerService;

    public TranslationPromptService(
        ILogger<TranslationPromptService> logger,
        ISpeakerManagementService speakerService)
    {
        _logger = logger;
        _speakerService = speakerService;
    }

    public (string systemPrompt, string userPrompt) BuildTranslationPrompts(EnhancedTranslationRequest request)
    {
        var systemPrompt = BuildSystemPrompt(request.SourceLanguage, request.TargetLanguage);
        var userPrompt = BuildUserPrompt(request);
        
        _logger.LogDebug("Built translation prompts for {SourceLang} -> {TargetLang}",
            request.SourceLanguage, request.TargetLanguage);
        
        return (systemPrompt, userPrompt);
    }

    private string BuildSystemPrompt(string primaryLang, string secondaryLang)
    {
        return $@"You are the **Main Intelligence Core** for a sophisticated real-time meeting assistant.

**OFFICIAL INPUT DATA STRUCTURE:**
You will receive a user message containing:
1. **Current Utterance**: The text to process.
2. **Acoustic Scorecard**: Mathematical similarity scores (0-100%) against known speakers.
3. **Speaker Context**: Profiles of known speakers (Style, Vocabulary).
4. **Conversation History**: The last 5 turns for context.
5. **Language Routing**: The active primary and secondary languages.

**YOUR CORE RESPONSIBILITIES (Execute in Order):**

1. 🕵️ **IDENTITY JUDGE (Priority #1)**:
   - **Goal**: definitive identification of the speaker.
   - **Input**: Use the 'Acoustic Scorecard' (Hard Evidence) + 'Conversation History' (Flow) + 'Linguistic Style' (Soft Evidence).
   - **Logic**:
     - *High Acoustic Score (>80%)* + *Context Fits* -> **CONFIRM_EXISTING**
     - *Low Acoustic Score* + *Context Mismatch* -> **NEW_SPEAKER**
     - *Ambiguous Acoustic*: Trust the **Conversation Flow** (Who was asked a question?) and **Style** (Who uses these words?).

2. 🌐 **TRANSLATION & ROUTING**:
   - Detect the input language.
   - **Routing Rule**: 
     - If {primaryLang} -> Translate to {secondaryLang}.
     - If {secondaryLang} -> Translate to {primaryLang}.
     - Else -> Translate to {primaryLang}.
   - **Output**: Provide natural, distinct translations.

3. 🧠 **INTENT & AI ASSISTANCE**:
   - **Intent**: 'SIMPLE_TRANSLATION' (Default) vs 'AI_ASSISTANCE' (User asks YOU for help).
   - **Trigger**: Only specific calls like ""Assistant, what did he say?"" or ""Translator, clarify that.""
   - **Action**: If triggered, provide a concise, helpful response in `aiAssistance`.

4. 📝 **FACT EXTRACTION**:
   - Extract strictly factual data: Dates, Deadlines, Names, Key Decisions.
   - Ignore small talk.

**STRICT JSON OUTPUT FORMAT**:
```json
{{
  ""improvedTranscription"": ""Cleaned text (no stutter)"",
  ""translation"": ""Target language translation"",
  ""intent"": ""SIMPLE_TRANSLATION"" | ""AI_ASSISTANCE"",
  ""translationLanguage"": ""Target BCP-47"",
  ""audioLanguage"": ""Detected Source BCP-47"",
  ""confidence"": 0.98,
  
  ""speakerIdentification"": {{
    ""decision"": ""CONFIRMED_EXISTING"" | ""NEW_SPEAKER"" | ""UNCERTAIN"",
    ""finalSpeakerId"": ""speaker_id"" or null,
    ""confidence"": 0.95,
    ""reasoning"": ""Scorecard showed 92% match for Speaker A, and context fits.""
  }},

  ""speakerProfileUpdate"": {{
    ""speakerId"": ""speaker_id"",
    ""suggestedName"": ""Extracted Name if introduced"",
    ""estimatedGender"": ""Male"" | ""Female"" | ""Unknown"",
    ""preferredLanguage"": ""Detected specific preference"",
    ""newVocabulary"": [""unique"", ""words""],
    ""tone"": ""formal"" | ""casual""
  }},

  ""aiAssistance"": {{
    ""triggerDetected"": true | false,
    ""response"": ""Your helpful answer"",
    ""responseTranslated"": ""Translated answer""
  }},

  ""factExtraction"": {{
    ""requiresFactExtraction"": true | false,
    ""facts"": [""Meeting at 5pm"", ""Budget approved""]
  }}
}}
```
";
    }

    private string BuildUserPrompt(EnhancedTranslationRequest request)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** \"{request.Text}\"");
        prompt.AppendLine();

        // Extract session language configuration from context
        string primaryLang = "en-US";
        string secondaryLang = "es-ES";
        string sessionId = request.SessionId ?? "unknown";
        
        if (request.SessionContext != null)
        {
            if (request.SessionContext.TryGetValue("primaryLanguage", out var primary))
                primaryLang = primary?.ToString() ?? "en-US";
            
            if (request.SessionContext.TryGetValue("secondaryLanguage", out var secondary))
                secondaryLang = secondary?.ToString() ?? "es-ES";
                
            if (request.SessionContext.TryGetValue("sessionId", out var sid))
                sessionId = sid?.ToString() ?? "unknown";
        }

        prompt.AppendLine($"### DYNAMIC LANGUAGE ROUTING:");
        prompt.AppendLine($"**Primary Language:** {primaryLang}");
        prompt.AppendLine($"**Secondary Language:** {secondaryLang}");
        prompt.AppendLine($"**Routing Rules:** Detected={primaryLang}→Target={secondaryLang}, Detected={secondaryLang}→Target={primaryLang}, Other→{primaryLang}");
        prompt.AppendLine();

        // 🚀 CLEAN: Add speaker context from speaker management service
        if (sessionId != "unknown")
        {
            var speakerContext = _speakerService.BuildSpeakerPromptContext(sessionId);
            prompt.AppendLine("### SPEAKER IDENTIFICATION CONTEXT:");
            prompt.AppendLine(speakerContext);
            prompt.AppendLine();
        }

        // Add acoustic signal if available
        if (request.SessionContext != null)
        {
            // 🚀 NEW: Render the Acoustic Scorecard
            if (request.SessionContext.TryGetValue("speakerScorecard", out var scorecardObj) 
                && scorecardObj is List<SpeakerComparisonResult> scorecard && scorecard.Count > 0)
            {
                prompt.AppendLine("### 🎯 ACOUSTIC SCORECARD (Mathematical Evidence):");
                foreach (var score in scorecard)
                {
                    prompt.AppendLine($"- **{score.DisplayName}** ({score.SpeakerId}): {score.CompositeScore:P0} Similarity (Pitch:{score.PitchSimilarity:P0}, Timbre:{score.TimbreSimilarity:P0})");
                }
            }
            else
            {
               prompt.AppendLine("### 🎯 ACOUSTIC SCORECARD:");
               prompt.AppendLine("No matches found (New acoustic profile).");
            }

            // 🚀 CONTEXT: Last 5 turns for flow analysis
            if (request.SessionContext.TryGetValue("recentHistory", out var historyObj) 
                && historyObj is List<ConversationHistoryItem> history && history.Count > 0)
            {
                prompt.AppendLine("### 📜 RECENT CONVERSATION HISTORY (Last 5 Turns):");
                foreach (var item in history)
                {
                    prompt.AppendLine($"- **{item.SpeakerName}** ({item.SpeakerId}): \"{item.Text}\"");
                }
            }
            else
            {
                prompt.AppendLine("### 📜 RECENT CONVERSATION HISTORY:");
                prompt.AppendLine("No previous history available.");
            }
            prompt.AppendLine();
        }
        
        prompt.AppendLine("### ANALYSIS INSTRUCTIONS:");
        prompt.AppendLine("1. **Language Detection**: Identify source language and apply routing rules");
        prompt.AppendLine("2. **Speaker Analysis**: Compare against known speakers above using voice + linguistic patterns");
        prompt.AppendLine("3. **Decision**: Determine if CONFIRMED_EXISTING, NEW_SPEAKER, or UNCERTAIN");
        prompt.AppendLine("4. **Translation**: Provide natural translation with proper target language");
        prompt.AppendLine("5. **Output**: Return complete JSON response with all analysis");
        
        return prompt.ToString();
    }
}


public class ConversationHistoryItem
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
