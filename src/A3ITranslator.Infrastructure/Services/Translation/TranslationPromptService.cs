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
2. **Acoustic Scorecard**: Neural similarity scores (Cosine Similarity: -1 to 1) against known speakers.
3. **Provisional ID**: The identity suggested by the live neural monitor (if a match was found early).
4. **Speaker Roster**: Detailed profiles of known speakers (ID, Name, Role, Style, Language).
5. **Conversation History**: The last 5 turns for context.
6. **Facts**: Derived facts from previous conversations.
7. **Expected Audio Language**: The language might be in the audio

**YOUR CORE RESPONSIBILITIES (Execute in Order):**

1. 🕵️ **ROSTER MANAGER (Priority #1)**:
   - **Goal**: Maintain a clean, accurate list of speakers. Avoid 'Ghost Speakers' caused by noise.
   - **Identity Decisions**:
     - **CONFIRM_EXISTING**: Acoustic score is high (>0.80) AND context fits the history.
     - **NEW_SPEAKER**: Acoustic scores are all low (<0.60) AND the context suggests a new participant.
     - **MERGE**: If you detect a 'New Speaker' but their language/gender/context matches an existing speaker who has very few utterances (a 'Ghost'), you must **MERGE** them into that primary speaker to clean the history.
   - **Strategy**: Trust the **Conversation Flow** (who was asked a question?) and **Social Roles** over weak acoustic signals.

2. 🌐 **TRANSLATION & ROUTING**:
   - Detect the input language (romanized or native script) .
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
  ""improvedTranscription"": ""Cleaned text (no stutter) in native script, as transcription could be romanized. Remove any duplication of sentences or words, and improve grammar/structure."",
  ""translation"": ""Target language translation for the improved text"",
  ""intent"": ""SIMPLE_TRANSLATION"" | ""AI_ASSISTANCE"",
  ""translationLanguage"": ""Target BCP-47"",
  ""audioLanguage"": ""Detected Source BCP-47"",
  ""confidence"": 0.98,
  
  ""turnAnalysis"": {{
    ""activeSpeakerId"": ""speaker_1"",
    ""identificationConfidence"": 0.98,
    ""decisionType"": ""CONFIRMED"" | ""NEW"" | ""MERGE"",
    ""mergeDetails"": {{
      ""ghostIdToRemove"": ""speaker_7"", 
      ""targetIdToKeep"": ""speaker_1""
    }}
  }},

  ""sessionRoster"": [
    {{
      ""speakerId"": ""speaker_1"",
      ""displayName"": ""Farhan"",
      ""socialRole"": ""Interviewer"" | ""Doctor"" | ""Client"" | ""Host"",
      ""estimatedGender"": ""Male"" | ""Female"" | ""Unknown"",
      ""preferredLanguage"": ""ur-PK"",
      ""tone"": ""formal"" | ""casual"",
      ""isLocked"": true 
    }}
  ],

  ""aiAssistance"": {{
    ""triggerDetected"": true | false,
    ""response"": ""Your helpful answer, it may be from facts, previous turns, context, model (generic), specific internet lookup for realtime info"",
    ""responseTranslated"": ""Translated AI response"",
    ""confidence"": 0.98
  }},

  ""factExtraction"": {{
    ""requiresFactExtraction"": true | false,
    ""facts"": [""Meeting at 5pm"", ""Budget approved""]      
  }}
}}
```";
    }

    private async Task<string> BuildUserPromptAsync(EnhancedTranslationRequest request)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** \"{request.Text}\"");
        prompt.AppendLine();

        string sessionId = request.SessionId;
        bool hasSession = !string.IsNullOrWhiteSpace(sessionId) && sessionId != "unknown";

        // 🚀 CLEAN: Add Neural Speaker Roster context
        if (hasSession)
        {
            var speakerContext = await _speakerService.BuildSpeakerPromptContextAsync(sessionId);
            prompt.AppendLine("### 📋 CURRENT SPEAKER ROSTER (Known Participants):");
            if (string.IsNullOrWhiteSpace(speakerContext) || speakerContext == "None.")
                prompt.AppendLine("- No participants identified yet.");
            else
                prompt.AppendLine(speakerContext);
            prompt.AppendLine();
        }

        // Add acoustic signal if available
        if (request.SessionContext != null)
        {
            // 🚀 NEW: Render Neural Provisional Evidence (What we heard during the stream)
            if (request.SessionContext.TryGetValue("provisionalId", out var provId) && provId != null)
            {
                var provName = request.SessionContext.TryGetValue("provisionalName", out var name) ? name : "Unknown";
                prompt.AppendLine("### 🧬 NEURAL PROVISIONAL EVIDENCE:");
                prompt.AppendLine($"- **PROVISIONAL MATCH**: The live monitor suggested **{provName}** ({provId})");
                prompt.AppendLine();
            }

            // 🚀 NEW: Expected Language Hint (from dual-STT selection)
            if (request.SessionContext.TryGetValue("expectedLanguageCode", out var expectedLang))
            {
                prompt.AppendLine($"**Expected Audio Language Hint:** {expectedLang}");
                prompt.AppendLine();
            }

            // 🚀 NEW: Render the Neural Scorecard
            if (request.SessionContext.TryGetValue("speakerScorecard", out var scorecardObj) 
                && scorecardObj is List<SpeakerComparisonResult> scorecard && scorecard.Count > 0)
            {
                prompt.AppendLine("### 🎯 ACOUSTIC SCORECARD (Neural Similarity):");
                foreach (var score in scorecard.Take(5))
                {
                    prompt.AppendLine($"- **{score.DisplayName}** ({score.SpeakerId}): {score.SimilarityScore:F4} Cosine Similarity");
                }
            }
            else
            {
               prompt.AppendLine("### 🎯 ACOUSTIC SCORECARD:");
               prompt.AppendLine("No strong neural matches found (Possibly a new speaker).");
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
        prompt.AppendLine("2. Use the **Neural Evidence** as a guide, but let the **Conversation History** be the final tie-breaker.");
        prompt.AppendLine("3. If this speaker matches an existing profile but the score is low due to audio quality, use **CONFIRM_EXISTING** if the context is 100% certain.");
        prompt.AppendLine("4. If this utterance belongs to a speaker you previously identified incorrectly, use the **MERGE** decision to fix it.");
        prompt.AppendLine("5. **Translation**: Provide natural translation with proper target language");
        prompt.AppendLine("6. **Fact Extraction**: Extract only NEW facts not already in 'EXISTING SESSION FACTS'");
        prompt.AppendLine("7. **Output**: Return complete JSON response with all analysis");
        
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
