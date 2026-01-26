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

    public async Task<(string systemPrompt, string userPrompt)> BuildTranslationPromptsAsync(EnhancedTranslationRequest request)
    {
        var systemPrompt = BuildSystemPrompt(request.SourceLanguage, request.TargetLanguage);
        var userPrompt = await BuildUserPromptAsync(request);
        
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
   - **Action**: If triggered, provide a concise, helpful response in `aiAssistance` in the audio language and translated response in the target language.

4. 📝 **FACT EXTRACTION**:
   - **Goal**: Extract only NEW, unique facts to avoid duplication.
   - **Context Awareness**: Review existing facts from previous conversation.
   - **Extract**: Dates, Deadlines, Names, Key Decisions, Important Details.
   - **Ignore**: Small talk, repeated information, previously mentioned facts.
   - **Rule**: Only add facts that are genuinely new or significantly different from existing ones.

**STRICT JSON OUTPUT FORMAT**:
```json
{{
  ""improvedTranscription"": ""Cleaned text (no stutter) in native script, as transcription could be romanized"",
  ""translation"": ""Target language translation"",
  ""intent"": ""SIMPLE_TRANSLATION"" | ""AI_ASSISTANCE"",
  ""translationLanguage"": ""Target BCP-47"",
  ""audioLanguage"": ""Detected Source BCP-47, the transcript could be in roman transcript as well, see possibilities form routing rules"",
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
    ""response"": ""Your helpful answer, it may be from facts, previous turns, context, model (generic), specific internet lookup for realtime info"",
    ""responseTranslated"": ""Translated answer"",
    ""confidence"": 0.98
  }},

  ""factExtraction"": {{
    ""requiresFactExtraction"": true | false,
    ""facts"": [""Meeting at 5pm"", ""Budget approved""]
  }}
}}
```
";
    }

    private async Task<string> BuildUserPromptAsync(EnhancedTranslationRequest request)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** \"{request.Text}\"");
        prompt.AppendLine();

        string sessionId = request.SessionId ?? "unknown";

        // 🚀 CLEAN: Add speaker context from speaker management service
        if (sessionId != "unknown")
        {
            var speakerContext = await _speakerService.BuildSpeakerPromptContextAsync(sessionId);
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

            // 🚀 NEW: Existing facts to prevent duplication
            if (request.SessionContext.TryGetValue("existingFacts", out var factsObj) 
                && factsObj is List<string> existingFacts && existingFacts.Count > 0)
            {
                prompt.AppendLine("### 📋 EXISTING SESSION FACTS (Do NOT duplicate these):");
                foreach (var fact in existingFacts)
                {
                    prompt.AppendLine($"- {fact}");
                }
                prompt.AppendLine("**Important**: Only extract NEW facts that are not already listed above.");
            }
            else
            {
                prompt.AppendLine("### 📋 EXISTING SESSION FACTS:");
                prompt.AppendLine("No facts extracted yet in this session.");
            }
            prompt.AppendLine();
        }
        
        prompt.AppendLine("### ANALYSIS INSTRUCTIONS:");
        prompt.AppendLine("1. **Language Detection**: Identify source language and apply routing rules");
        prompt.AppendLine("2. **Speaker Analysis**: Compare against known speakers above using voice + linguistic patterns");
        prompt.AppendLine("3. **Decision**: Determine if CONFIRMED_EXISTING, NEW_SPEAKER, or UNCERTAIN");
        prompt.AppendLine("4. **Translation**: Provide natural translation with proper target language");
        prompt.AppendLine("5. **Fact Extraction**: Extract only NEW facts not already in 'EXISTING SESSION FACTS'");
        prompt.AppendLine("6. **Output**: Return complete JSON response with all analysis");
        
        return prompt.ToString();
    }

    public Task<(string systemPrompt, string userPrompt)> BuildSummaryPromptsAsync(string conversationHistory, string primaryLanguage, string secondaryLanguage)
    {
        var systemPrompt = $@"You are an expert meeting summarizer for a real-time translation assistant.
Your goal is to provide a concise, professional summary (Insights) of the conversation provided.

**SUMMARY FORMAT (Markdown):**
1. **Date**: (Extract if mentioned, else use 'Current Session')
2. **Meeting Place**: (Extract if mentioned, else 'Not specified')
3. **Meeting Heading**: (Create a short, descriptive title)
4. **Purpose**: (What was the primary goal of the meeting?)
5. **Participants**: (List names or roles. If names are unknown, list as 'Speaker 1', 'Speaker 2', etc. with minimum count possible)
6. **Key Discussion Points**: (Bullet points of what was discussed)
7. **Conclusion & Actions**: (What was decided and what are the next steps?)

**LANGUAGE REQUIREMENT:**
- You must provide the FULL summary in BOTH {primaryLanguage} AND {secondaryLanguage}.
- Present {primaryLanguage} summary first, then {secondaryLanguage} summary.
- Use clear headings to separate the languages.

**OUTPUT:**
Provide only the Markdown formatted summary.";

        var userPrompt = $@"Please summarize the following conversation history:

### CONVERSATION HISTORY:
{conversationHistory}

### LANGUAGES:
- Primary: {primaryLanguage}
- Secondary: {secondaryLanguage}";

        return Task.FromResult((systemPrompt, userPrompt));
    }
}


public class ConversationHistoryItem
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
