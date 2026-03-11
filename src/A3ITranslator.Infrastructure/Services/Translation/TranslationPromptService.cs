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



    public Task<(string systemPrompt, string userPrompt)> BuildFastIntentPromptsAsync(string transcription)
    {
        var systemPrompt = @"You are an ultra-fast intent router.
**YOUR ONLY RESPONSIBILITY:**
Determine if the user is explicitly addressing the AI assistant to ask a question or request help.
Triggers include names like: ""Assistant"", ""Smart Tolk"", ""Tolk"", ""AI"", ""اسسٹنٹ"", ""ٹولک"", ""سمارٹ ٹولک"".
If the user is just talking normally, return false.
If the user is asking the AI a factual question, return true.

**STRICT OUTPUT FORMAT:**
Respond with EXACTLY the word ""true"" or ""false"". No other text.";

        var userPrompt = $"**Transcription:** \"{transcription}\"";

        return Task.FromResult((systemPrompt, userPrompt));
    }

    public async Task<(string systemPrompt, string userPrompt)> BuildAgent2PromptsAsync(EnhancedTranslationRequest request)
    {
        var primaryLang = request.SourceLanguage;
        var secondaryLang = request.TargetLanguage;
        var systemPrompt = $@"You are the Intelligence Core Agent for a real-time meeting.
**YOUR CORE RESPONSIBILITIES:**
1. **CONTEXTUAL TRANSLATION & ROUTING**:
   - Detect the input language and leverage **History** and **facts** to understand hidden meaning or jargon.
   - **Routing Rule**: 
     - If {primaryLang} -> Translate to {secondaryLang}.
     - If {secondaryLang} -> Translate to {primaryLang}.
     - Else -> Translate to {primaryLang}.
   - **Output**: Provide a translation that captures the **Contextual Nuance** and **Industry Specific Meaning**, BUT expressed in SIMPLE, everyday language. Pulse track handles literal meaning; YOU handle the deeper intent but keep the wording accessible.

2. **INTENT & AI ASSISTANCE**:
   - **Intent**: 'SIMPLE_TRANSLATION' (Default) vs 'AI_ASSISTANCE' (User asks YOU for help).
   - **Trigger**: Only specific calls like, if the user explicitly addresses you , in any given language (expect accents and variations of the given language)(e.g. ""Assistant"", ""Smart Tolk"", ""Tolk"", ""اسسٹنٹ"", ""ٹولک"", ""اسسٹنٹ"", ""سمارٹ ٹولک"", ""ٹولک"") and asks a question meant for the AI.
   - **Action**: 
     - If triggered, you may use **Google Search Grounding** to find real-time info (e.g., current CEOs, weather, news, events, time).
     - **PERSONALIZATION**: Look at the **PROVISIONAL MATCH** in the Neural Evidence section.
       - If a name is provided (e.g., ""Farhan"", ""John"") and it is NOT Unknown, you MUST start your response by addressing them (e.g., ""Hello Farhan, ..."").
       - If the name is Unknown or no match exists, reply DIRECTLY to the query without any greeting.
     - Provide a concise, helpful response in `aiAssistance` in the transcription language and translated response in the target language. KEEP IT SIMPLE and easy to understand.
     - **DO NOT** use search for simple translations or pleasantries. Only for factual questions.
3. **ROSTER MANAGER (Priority #1)**:
   - **Goal**: Maintain a clean, accurate list of speakers. Avoid 'Ghost Speakers' caused by noise.
   - **Identity Decisions**:
     - **CONFIRM_EXISTING**: Acoustic score is high (>0.80) AND context fits the history.
     - **NEW_SPEAKER**: Acoustic scores are all low (<0.60) AND the context suggests a new participant.
     - **MERGE**: If you detect a 'New Speaker' but their language/gender/context matches an existing speaker who has very few utterances (a 'Ghost'), you must **MERGE** them into that primary speaker to clean the history.
   - **Strategy**: Trust the **Conversation Flow** (who was asked a question?) and **Social Roles** over weak acoustic signals. DO NOT trust the PROVISIONAL MATCH if the text explicitly indicates a new speaker introducing themselves or a clear change in dialogue flow.
   - **Linguistic Clues**: Pay extremely close attention to the **Vocabulary**, **Tone**, and **Preferred Language** of the speaker. **CRITICAL: If the language of the current utterance changes compared to the previous turn, strongly expect a DIFFERENT speaker.** **HOWEVER**, be highly aware of **Code-Switching**. If the speaker is known to speak {primaryLang} but throws in common phrases from {secondaryLang} (e.g., an Urdu speaker using English terms), DO NOT flag them as a new speaker. Look at the flow of the conversation over strict language boundaries. If the current transcript heavily uses technical industry jargon matching Speaker A, but acoustic scores are weak, trust the vocabulary and assign it to Speaker A.

**STRICT JSON OUTPUT FORMAT**:
```json
{{
  ""improvedTranscription"": ""Cleaned text removing stutters and fillers"",
  ""translation"": ""Deep contextual translation accounting for roles, history, and jargon"",
  ""estimatedGender"": ""Male"" | ""Female"" | ""Unknown"",
  ""confidence"": 0.98,
  
  ""aiAssistance"": {{
     ""triggerDetected"": true,
     ""response"": ""Your helpful AI answer based on facts/history"",
     ""responseTranslated"": ""Translated AI response"",
     ""confidence"": 0.98
  }},

  ""turnAnalysis"": {{
    ""activeSpeakerId"": ""speaker_1"",
    ""identificationConfidence"": 0.98,
    ""decisionType"": ""CONFIRMED"" | ""NEW"" | ""MERGE"",
    ""mergeDetails"": {{ ""ghostIdToRemove"": ""speaker_7"", ""targetIdToKeep"": ""speaker_1"" }}
  }},
  
  ""sessionRoster"": [
     {{
      ""speakerId"": ""speaker_1"",
      ""displayName"": ""Farhan"",
      ""socialRole"": ""Interviewer"" | ""Doctor"" | ""Client"" | ""Host"",
      ""estimatedGender"": ""Male"" | ""Female"" | ""Unknown"",
      ""preferredLanguage"": ""ur-PK"",
      ""tone"": ""formal"" | ""casual"",
      ""isLocked"": true,
      ""isNameExplicitlyCorrected"": false
    }}
  ]
}}
```";
        
        var prompt = new StringBuilder();
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** \"{request.Text}\"");
        prompt.AppendLine();

        string sessionId = request.SessionId;
        bool hasSession = !string.IsNullOrWhiteSpace(sessionId) && sessionId != "unknown";

        if (hasSession)
        {
            var speakerContext = await _speakerService.BuildSpeakerPromptContextAsync(sessionId);
            prompt.AppendLine("### 📋 CURRENT SPEAKER ROSTER:");
            if (string.IsNullOrWhiteSpace(speakerContext) || speakerContext == "None.") prompt.AppendLine("- No participants identified yet.");
            else prompt.AppendLine(speakerContext);
            prompt.AppendLine();
        }

        if (request.SessionContext != null)
        {
            if (request.SessionContext.TryGetValue("provisionalId", out var provId) && provId != null)
            {
                var provName = request.SessionContext.TryGetValue("provisionalName", out var name) ? name : "Unknown";
                prompt.AppendLine("### 🧬 NEURAL PROVISIONAL EVIDENCE:");
                prompt.AppendLine($"- **PROVISIONAL MATCH**: The live monitor suggested **{provName}** ({provId}). Use this to greet the user if providing AI Assistance.");
                prompt.AppendLine();
            }
            if (request.SessionContext.TryGetValue("expectedLanguageCode", out var expectedLang)) prompt.AppendLine($"**Expected Audio Language Hint:** {expectedLang}\n");
            
            if (request.SessionContext.TryGetValue("speakerScorecard", out var scorecardObj) && scorecardObj is List<SpeakerComparisonResult> scorecard && scorecard.Count > 0)
            {
                prompt.AppendLine("### 🎯 ACOUSTIC SCORECARD (Neural Similarity):");
                foreach (var score in scorecard.Take(5)) prompt.AppendLine($"- **{score.DisplayName}** ({score.SpeakerId}): {score.SimilarityScore:F4} Cosine");
                prompt.AppendLine();
            }

            if (request.SessionContext.TryGetValue("facts", out var factsObj) && factsObj is List<FactItem> facts && facts.Count > 0)
            {
                prompt.AppendLine("### 🧩 KNOWN FACTS (Use for AI Assistance):");
                foreach (var fact in facts) prompt.AppendLine($"- **{fact.Key}**: {fact.Value}");
                prompt.AppendLine();
            }

            if (request.SessionContext.TryGetValue("recentHistory", out var historyObj) && historyObj is List<ConversationHistoryItem> history && history.Count > 0)
            {
                prompt.AppendLine("### 📜 RECENT CONVERSATION (Use for AI Assistance & Speaker ID):");
                foreach (var item in history) prompt.AppendLine($"- {item.SpeakerName}: \"{item.Text}\"");
                prompt.AppendLine();
            }
        }
        return (systemPrompt, prompt.ToString());
    }

    public Task<(string systemPrompt, string userPrompt)> BuildAgent3PromptsAsync(EnhancedTranslationRequest request)
    {
        var systemPrompt = $@"You are the Background Librarian (Fact Manager) for a meeting.
**YOUR CORE RESPONSIBILITIES:**
1. **FACT MANAGER**:
   - Extract facts directly from the 'Current Utterance'.
   - Keep keys and values in English. Use operations ADD, UPDATE, or DELETE.
   - use first 5-10 sentences to setup meeting context, by adding speakers, purpose of the meeting and are where it belongs and all important metadata about meeting

**STRICT JSON OUTPUT FORMAT**:
```json
{{
  ""factExtraction"": {{
    ""facts"": [
      {{
        ""key"": ""Fact Key"",
        ""value"": ""Fact Value"",
        ""operation"": ""ADD"" | ""UPDATE"" | ""DELETE""
      }}
    ]
  }}
}}
```";
        
        var prompt = new StringBuilder();
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** \"{request.Text}\"");
        prompt.AppendLine($"**Estimated Transcription Language (STT Winner):** {request.SourceLanguage}");
        prompt.AppendLine();

        if (request.SessionContext != null)
        {
            if (request.SessionContext.TryGetValue("provisionalId", out var provId) && provId != null)
            {
                var provName = request.SessionContext.TryGetValue("provisionalName", out var name) ? name : "Unknown";
                prompt.AppendLine("### 🧬 NEURAL PROVISIONAL EVIDENCE:");
                prompt.AppendLine($"- **PROVISIONAL MATCH**: The live monitor suggested **{provName}** ({provId}). Use this information for fact extraction context if relevant.");
                prompt.AppendLine();
            }

            if (request.SessionContext.TryGetValue("facts", out var factsObj) && factsObj is List<FactItem> facts && facts.Count > 0)
            {
                prompt.AppendLine("### 🧩 KNOWN FACTS:");
                foreach (var fact in facts) prompt.AppendLine($"- **{fact.Key}**: {fact.Value}");
                prompt.AppendLine();
            }

            if (request.SessionContext.TryGetValue("recentHistory", out var historyObj) && historyObj is List<ConversationHistoryItem> history && history.Count > 0)
            {
                prompt.AppendLine("### 📜 RECENT CONVERSATION HISTORY (Last 5 Turns):");
                foreach (var item in history) prompt.AppendLine($"- **{item.SpeakerName}**: \"{item.Text}\"");
            }
        }
        
        return Task.FromResult((systemPrompt, prompt.ToString()));
    }

    public Task<(string systemPrompt, string userPrompt)> BuildNativeSummaryPromptsAsync(
        string conversationHistory, 
        string language)
    {
        var langName = LanguageConfigurationService.GetLanguageDisplayName(language);
        bool isRTL = LanguageConfigurationService.IsRightToLeft(language);
        
        var systemPrompt = $@"You are an expert meeting summarizer.
Generate a professional summary **entirely in {langName}** ({language}).
**AUDIENCE CONSTRAINT**: The summary must be written in **simple, basic, day-to-day language** suitable for a low-educated audience. Use clear, common words and local variants if applicable.

**CRITICAL FORMATTING REQUIREMENTS:**
1. **NO MARKDOWN IN KEYS**: Do NOT use asterisks (*) or brackets ([]) for the section headers. Reference the example below exactly.
2. **Structure**: Use clear key-value pairs where the key is the section name in {langName}.
3. **Content**: All values must be in {langName}.

{(isRTL ? "4. **RIGHT-TO-LEFT**: Ensure proper RTL flow for this language." : "")}

**REQUIRED OUTPUT FORMAT (Translate keys to {langName}):**
LabelDate:session start date time
LabelLocation:Location label in {langName}
LabelTitle:Title label in {langName}
LabelObjective:Objective label in {langName}
LabelParticipants:Participants label in {langName}
LabelKeyDiscussionPoints:KeyDiscussionPoints label in {langName}
LabelActionItems:ActionItems label in {langName}

Date: [Date content]
Location: [Location content]
Title: [Title content]
Objective: [Objective content]

Participants:
- [Name 1]
- [Name 2]

Key Discussion Points:
- [Point 1]
- [Point 2]

Action Items:
- [Action 1]
- [Action 2]

**DO NOT** add bounding boxes, citation markers, or markdown bolding to the section labels (e.g. write 'Date:', NOT '**Date**:').";

        var userPrompt = $@"### CONVERSATION HISTORY:
{conversationHistory}

### TASK:
Generate the meeting summary in {langName} following the strict format above. Ensure no asterisks are used in the section headers. make it maximum 2 page summary but try to keep it short but proportional to siginficant discussion in connection of the goal of the meeting ";

        return Task.FromResult((systemPrompt, userPrompt));
    }
}


public class ConversationHistoryItem
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
