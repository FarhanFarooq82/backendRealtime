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
        var systemPrompt = BuildSystemPrompt(request.SourceLanguage, request.TargetLanguage, request.IsPulse);
        var userPrompt = await BuildUserPromptAsync(request);
        
        _logger.LogDebug("Built {PromptType} translation prompts for {SourceLang} -> {TargetLang}",
            request.IsPulse ? "PULSE" : "BRAIN", request.SourceLanguage, request.TargetLanguage);
        
        return (systemPrompt, userPrompt);
    }

    private string BuildSystemPrompt(string primaryLang, string secondaryLang, bool isPulse)
    {
        if (isPulse)
        {
            return $@"You are the **Fast Translation Pulse** for a real-time meeting assistant.
Your goal is to provide a clean, immediate, and speakable translation optimized for Text-to-Speech (TTS).

**CORE RESPONSIBILITIES:**
1. 🌐 **TRANSLATION**:
   - Detect input language.
   - If {primaryLang} -> Translate to {secondaryLang}.
   - If {secondaryLang} -> Translate to {primaryLang}.
   - If audio language is unknown -> Translate to {primaryLang}.
   - Content: Provide natural, distinct translations.
   - Grammar: Fix stutters and improve basic structure.

2. 🧠 **INTENT DETECTION**:
   - Intent: 'SIMPLE_TRANSLATION' (Default) or 'AI_ASSISTANCE' (User asks a question like 'Assistant...', 'Translator...').

**STRICT JSON OUTPUT FORMAT**:
{{
  ""improvedTranscription"": ""Cleaned text in native script, as transcription could be romanized. Remove any duplication of sentences or words, and improve grammar/structure."",
  ""translation"": ""Target translation"",
  ""intent"": ""SIMPLE_TRANSLATION"" | ""AI_ASSISTANCE"",
  ""translationLanguage"": ""Target BCP-47""
}}";
}

        return $@"You are the **Main Intelligence Core** for a sophisticated real-time meeting assistant.

**OFFICIAL INPUT DATA STRUCTURE:**
You will receive a user message containing:
1. **Current Utterance**: The text to process.
2. **Acoustic Scorecard**: Neural similarity scores (Cosine Similarity: -1 to 1) against known speakers.
3. **Provisional ID**: The identity suggested by the live neural monitor (if a match was found early).
4. **Speaker Roster**: Detailed profiles of known speakers (ID, Name, Role, Style, Language).
5. **Conversation**: The last 5 turns for context.
6. **Facts**: Derived facts from previous conversations.
7. **Expected Audio Language**: The language might be in the audio.
8. **Acoustic/STT Confidence**: Reliability scores for the input audio and text.

**YOUR CORE RESPONSIBILITIES (Execute in Order):**

1. 🕵️ **ROSTER MANAGER (Priority #1)**:
   - **Goal**: Maintain a clean, accurate list of speakers. Avoid 'Ghost Speakers' caused by noise.
   - **Identity Decisions**:
     - **CONFIRM_EXISTING**: Acoustic score is high (>0.80) AND context fits the history.
     - **NEW_SPEAKER**: Acoustic scores are all low (<0.60) AND the context suggests a new participant.
     - **MERGE**: If you detect a 'New Speaker' but their language/gender/context matches an existing speaker who has very few utterances (a 'Ghost'), you must **MERGE** them into that primary speaker to clean the history.
   - **Strategy**: Trust the **Conversation Flow** (who was asked a question?) and **Social Roles** over weak acoustic signals.

2. 🌐 **CONTEXTUAL TRANSLATION & ROUTING**:
   - Detect the input language and leverage **History** and **Significant Turns** to understand hidden meaning or jargon.
   - **Routing Rule**: 
     - If {primaryLang} -> Translate to {secondaryLang}.
     - If {secondaryLang} -> Translate to {primaryLang}.
     - Else -> Translate to {primaryLang}.
   - **Output**: Provide a translation that captures the **Contextual Nuance** and **Industry Specific Meaning**. Pulse track handles literal meaning; YOU handle the deeper intent.

3. 🧠 **INTENT & AI ASSISTANCE**:
   - **Intent**: 'SIMPLE_TRANSLATION' (Default) vs 'AI_ASSISTANCE' (User asks YOU for help).
   - **Trigger**: Only specific calls like ""Assistant, what did he say?"" or ""Translator, clarify that.""
   - **Action**: If triggered, provide a concise, helpful response in `aiAssistance` in the audio language and translated response in the target language.

4. 📝 **SIGNIFICANT INFO DETECTION**:
   - **Goal**: Identify if this utterance contains high-value information.
   - **Criteria**: Dates, Deadlines, Names, Key Decisions, Important Details, Budget, Phone Numbers, Addresses, Key Person, Key relations.
   - **Action**: Set `hasSignificantInfo` to true if the turn contains such data, false otherwise.

**STRICT JSON OUTPUT FORMAT**:
```json
{{
  ""improvedTranscription"": ""Cleaned text (no stutter) in native script. Keep this simple; the Pulse track already provided the baseline."",
  ""translation"": ""DEEP CONTEXTUAL translation that accounts for speaker roles, jargon, and previous turns. Capture the 'true meaning' over literal words."",
  ""intent"": ""SIMPLE_TRANSLATION"" | ""AI_ASSISTANCE"",
  ""estimatedGender"": ""Male"" | ""Female"" | ""Unknown"",
  ""translationLanguage"": ""Target BCP-47"",
  ""audioLanguage"": ""Detected Source BCP-47"",
  ""confidence"": 0.98,
  ""hasSignificantInfo"": true | false,
  
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
    ""hasSignificantInfo"": true | false
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

        if (request.IsPulse)
        {
            prompt.AppendLine("### 📋 SUMMARY CONTEXT:");
            if (request.SessionContext != null && request.SessionContext.TryGetValue("summary", out var summary))
                prompt.AppendLine(summary.ToString());
            else
                prompt.AppendLine("No summary available yet.");
                
            if (request.SessionContext != null && request.SessionContext.TryGetValue("recentHistory", out var historyObj) 
                && historyObj is List<ConversationHistoryItem> history && history.Count > 0)
            {
                prompt.AppendLine("### 📜 RECENT CONVERSATION HISTORY (Last 5 Turns):");
                foreach (var item in history)
                {
                    prompt.AppendLine($"- **{item.SpeakerName}** ({item.SpeakerId}): \"{item.Text}\"");
                }
                prompt.AppendLine();
            }
            
            // 🚀 NEW: Expected Language Hint (from dual-STT selection)
            if (request.SessionContext.TryGetValue("expectedLanguageCode", out var expectedLang))
            {
                prompt.AppendLine($"**Expected Audio Language Hint:** {expectedLang}");
                prompt.AppendLine();
            }

            prompt.AppendLine("### ANALYSIS INSTRUCTIONS:");
            prompt.AppendLine("1. **Translate** accurately for TTS (ensure it flows naturally when spoken).");
            prompt.AppendLine("2. **Detect Intent**: Is the user talking to a human or the AI?");
            prompt.AppendLine("3. **Gender Estimation**: Provide the speaker's likely gender for better voice selection.");
            prompt.AppendLine("4. **Output**: Return lean JSON only.");
            return prompt.ToString();
        }

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
            
            // 🚀 NEW: Confidence Scores (Hints for refinement)
            prompt.AppendLine("### 📊 INPUT RELIABILITY:");
            prompt.AppendLine($"- **Transcription Confidence**: {(request.SessionContext.TryGetValue("transcriptionConfidence", out var tc) ? (float)tc : 0f):P0}");
            prompt.AppendLine($"- **Speaker Matching Confidence**: {(request.SessionContext.TryGetValue("speakerConfidence", out var sc) ? (float)sc : 0f):P0}");
            prompt.AppendLine();

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

            // 🚀 NEW: Significant Historical Turns (High-value context for AI questions)
            if (request.SessionContext.TryGetValue("significantHistory", out var sigObj) 
                && sigObj is List<ConversationHistoryItem> sigHistory && sigHistory.Count > 0)
            {
                prompt.AppendLine("### 💎 SIGNIFICANT CONVERSATION TURNS (High-Value context):");
                foreach (var item in sigHistory)
                {
                    prompt.AppendLine($"- **{item.SpeakerName}** ({item.SpeakerId}): \"{item.Text}\"");
                }
            }
            prompt.AppendLine();
        }
        
        prompt.AppendLine("### ANALYSIS INSTRUCTIONS:");
        prompt.AppendLine("1. **Language Detection**: Identify source language and apply routing rules");
        prompt.AppendLine("2. Use the **Neural Evidence** as a guide, but let the **Conversation History** be the final tie-breaker.");
        prompt.AppendLine("3. If this speaker matches an existing profile but the score is low due to audio quality, use **CONFIRM_EXISTING** if the context is 100% certain.");
        prompt.AppendLine("4. If this utterance belongs to a speaker you previously identified incorrectly, use the **MERGE** decision to fix it.");
        prompt.AppendLine("5. **Translation**: Provide natural translation with proper target language");
        prompt.AppendLine("6. **Significant Info**: Mark as true if high-value information is present");
        prompt.AppendLine("7. **Output**: Return complete JSON response with all analysis");
        
        return prompt.ToString();
    }

    public Task<(string systemPrompt, string userPrompt)> BuildNativeSummaryPromptsAsync(
        string conversationHistory, 
        string language)
    {
        var langName = LanguageConfigurationService.GetLanguageDisplayName(language);
        bool isRTL = LanguageConfigurationService.IsRightToLeft(language);
        
        var systemPrompt = $@"You are an expert meeting summarizer.
Generate a professional summary **entirely in {langName}** ({language}).

**CRITICAL REQUIREMENTS:**
1. **ALL content must be in {langName}** - headings, labels, and body text
2. **Use native terminology** - Choose culturally appropriate section names for:
   - Date/Time information
   - Meeting location
   - Meeting title
   - Purpose/Objective
   - List of participants
   - Key discussion points
   - Conclusions and action items

{(isRTL ? "3. **This is a RIGHT-TO-LEFT language** - Ensure proper RTL text flow" : "")}

**FORMAT:**
Use clear markdown structure with:
- Bold headings for each section (using native language terms)
- Bullet points for lists
- Professional, concise language

**EXAMPLE STRUCTURE (translate section names to {langName}):**
**[Date label in {langName}]**: ...
**[Meeting Place label in {langName}]**: ...
**[Meeting Title label in {langName}]**: ...
**[Purpose label in {langName}]**: ...
**[Participants label in {langName}]**: ...
**[Key Points label in {langName}]**: ...
**[Actions label in {langName}]**: ...

OUTPUT: Provide ONLY the formatted summary in {langName}.";

        var userPrompt = $@"### CONVERSATION HISTORY:
{conversationHistory}

### TASK:
Generate a complete meeting summary in {langName} ({language}) with ALL headings and content in the native language.";

        return Task.FromResult((systemPrompt, userPrompt));
    }
}


public class ConversationHistoryItem
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
