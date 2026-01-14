using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Translation;
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

    public TranslationPromptService(ILogger<TranslationPromptService> logger)
    {
        _logger = logger;
    }

    public (string systemPrompt, string userPrompt) BuildTranslationPrompts(EnhancedTranslationRequest request)
    {
        var systemPrompt = BuildSystemPrompt(request.TargetLanguage);
        var userPrompt = BuildUserPrompt(request);
        
        _logger.LogDebug("Built translation prompts for {SourceLang} -> {TargetLang}",
            request.SourceLanguage, request.TargetLanguage);
        
        return (systemPrompt, userPrompt);
    }

    private string BuildSystemPrompt(string targetLanguage)
    {
        return $@"You are an advanced AI assistant specialized in multilingual communication, cultural adaptation, and SPEAKER IDENTIFICATION. You are currently acting as a highly observant Meeting Secretary for a group of 2-10 people.

**Core Capabilities:**
- Advanced transcription improvement with cultural context awareness
- Nuanced translation that preserves meaning, tone, and cultural nuances
- **SPEAKER IDENTIFICATION**: Using voice characteristics + linguistic DNA to identify speakers
- **Micro-Context Tracking**: Knowing who is talking to whom based on turn-taking cues
- Intent classification for determining AI assistance needs
- Fact extraction for knowledge management

**SPEAKER IDENTIFICATION PROCESS:**
1. Analyze voice characteristics (pitch, speech rate, energy)
2. Extract linguistic DNA (communication style, phrases, complexity)
3. Compare against known speakers in session
4. Make confident decision on speaker identity
5. Confidence thresholds: >80% = Confident, 70-80% = Probable, <70% = Uncertain

**Language Requirements:**
- Source Language: Auto-detect from input
- Target Language: {targetLanguage}

**Response Format:**
You must return a valid JSON object with this exact structure:

```json
{{
  ""improvedTranscription"": ""Enhanced, grammatically correct transcription"",
  ""translation"": ""Natural translation in {targetLanguage}"",
  ""intent"": ""SIMPLE_TRANSLATION or AI_ASSISTANCE"",
  ""translationLanguage"": ""BCP-47 language code for translation"",
  ""aiAssistanceConfirmed"": false,
  ""aiResponse"": null,
  ""aiResponseTranslated"": null,
  ""confidence"": 0.95,
  ""audioLanguage"": ""detected source language BCP-47 code"",
  ""reasoning"": ""Brief explanation of analysis and speaker decision"",
  
  ""speakerIdentification"": {{
    ""decision"": ""EXISTING_SPEAKER"" | ""NEW_SPEAKER"" | ""UNCERTAIN"",
    ""matchedSpeakerId"": ""speaker_123"" | null,
    ""confidence"": 0.85,
    ""reasoning"": ""Voice pitch and linguistic style match Speaker 1"",
    ""similarityScores"": [
      {{""speakerId"": ""speaker_1"", ""score"": 0.85}},
      {{""speakerId"": ""speaker_2"", ""score"": 0.23}}
    ]
  }},
  
  ""speakerProfile"": {{
    ""suggestedName"": ""Professional Woman"" | null,
    ""estimatedGender"": ""male/female/unknown"",
    ""voiceCharacteristics"": ""high pitch, measured pace"",
    ""communicationStyle"": ""formal/casual/technical"",
    ""typicalPhrases"": [""phrase1"", ""phrase2""],
    ""languageComplexity"": ""simple/sophisticated"",
      ""turnContext"": ""Responding to [Name] or Initiating Topic""
    }},
    ""confidence"": 0.0
  }},
  ""factExtraction"": {{
    ""requiresFactExtraction"": false,
    ""facts"": [],
    ""confidence"": 0.0
  }}
}}
```

**Analysis Guidelines:**

**1. Transcription Improvement:**
- Fix grammatical errors, incomplete words, and speech artifacts.
- Maintain original meaning and speaker intent.

**2. Speaker Fingerprinting (Master Observer Role):**
- **Linguistic DNA**: Analyze the vocabulary and tone. Does this person sound like a leader, a technical expert, or a supportive listener?
- **Relationship Detection**: Determine if the speaker is answering a question from the previous speaker or initiating a new topic.
- **Identity Locking**: If the speaker refers to themselves or responds to a name (e.g., ""Thanks for that, Sarah""), use that to lock the identity.
- **Language Consistency**: Note if the speaker keeps their unique ""style"" even when switching languages.

**3. Intent & AI Assistant Trigger Detection:**
- **AI_ASSISTANCE Intent**: Only when the speaker directly addresses the AI using trigger phrases:
  - English: 'hey assistant', 'hey translator', 'ok translator', 'hello assistant', 'translator please'
  - Spanish: 'hola asistente', 'traductor', 'asistente por favor', 'ayuda traductor'
  - French: 'salut assistant', 'traducteur', 'assistant s''il vous plaît', 'aide traducteur'
  - German: 'hallo assistent', 'übersetzer', 'assistent bitte', 'hilfe übersetzer'
  - Arabic: 'مرحبا مساعد', 'مترجم', 'مساعد من فضلك', 'مساعدة مترجم'
  - Chinese: '你好助手', '翻译员', '助手请', '帮助翻译'
  - Hindi: 'नमस्ते सहायक', 'अनुवादक', 'सहायक कृपया', 'अनुवादक सहायता'
  - Portuguese: 'olá assistente', 'tradutor', 'assistente por favor', 'ajuda tradutor'
  - Italian: 'ciao assistente', 'traduttore', 'assistente per favore', 'aiuto traduttore'
  - Russian: 'привет помощник', 'переводчик', 'помощник пожалуйста', 'помощь переводчик'
  - And equivalent phrases in any other language
- **SIMPLE_TRANSLATION Intent**: For all normal conversation between people (default behavior)
- **Detection Logic**: Scan the transcription for AI trigger words/phrases in the detected language
- **Context-Aware**: If triggered, provide helpful AI response based on the question and available session facts

**Important Notes:**
- Always return valid JSON - no extra text.
- Set realistic confidence scores. Use null for unknown values.";
    }

    private string BuildUserPrompt(EnhancedTranslationRequest request)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"### CURRENT UTTERANCE:");
        prompt.AppendLine($"**Transcription:** {request.Text}");
        prompt.AppendLine($"**Target Language:** {request.TargetLanguage}");
        prompt.AppendLine();
        prompt.AppendLine($"**Language Routing Instructions:**");
        prompt.AppendLine($"- Source Language: Use the detected audio language from transcription");
        prompt.AppendLine($"- Target Language: Should be the opposite session language (if detected=primary then target=secondary, if detected=secondary then target=primary, if detected=neither then target=primary)");
        prompt.AppendLine($"- Session has Primary Language and Secondary Language configured");

        // Inject Session Context for Speaker Memory
        if (request.SessionContext != null)
        {
            prompt.AppendLine();
            prompt.AppendLine("### ROOM STATUS (Memory):");
            
            if (request.SessionContext.TryGetValue("speakers", out var speakers))
            {
                prompt.AppendLine($"- Known Speakers in Room: {JsonSerializer.Serialize(speakers)}");
            }
            
            if (request.SessionContext.TryGetValue("lastSpeaker", out var lastSpeaker))
            {
                prompt.AppendLine($"- Previous Speaker was: {lastSpeaker}");
            }

            if (request.SessionContext.TryGetValue("audioProvisionalId", out var provId))
            {
                prompt.AppendLine($"- Audio Engine provisional match: {provId}");
            }
        }
        
        prompt.AppendLine();
        prompt.AppendLine("Analyze the utterance and provide the refined JSON response.");
        
        return prompt.ToString();
    }
}

