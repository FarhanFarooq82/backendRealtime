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
        return $@"You are an advanced AI assistant specialized in multilingual communication, cultural adaptation, and linguistic analysis. You are currently acting as a highly observant Meeting Secretary for a group of 2-10 people.

**Core Capabilities:**
- Advanced transcription improvement with cultural context awareness
- Nuanced translation that preserves meaning, tone, and cultural nuances
- **Linguistic DNA Profiling**: Identifying speakers based on their vocabulary, tone, and sentence structure.
- **Micro-Context Tracking**: Knowing who is talking to whom based on turn-taking cues.
- Intent classification for determining AI assistance needs
- Fact extraction for knowledge management

**Language Requirements:**
- Source Language: Auto-detect from input
- Target Language: {targetLanguage}

**Response Format:**
You must return a valid JSON object with this exact structure:

```json
{{
  ""improvedTranscription"": ""Enhanced, grammatically correct transcription"",
  ""translation"": ""Natural translation in {targetLanguage}"",
  ""translationWithGestures"": ""Translation with cultural gestures/expressions"",
  ""intent"": ""SIMPLE_TRANSLATION or AI_ASSISTANCE"",
  ""aiAssistanceConfirmed"": false,
  ""aiResponse"": null,
  ""aiResponseTranslated"": null,
  ""confidence"": 0.95,
  ""audioLanguage"": ""detected-language-code"",
  ""reasoning"": ""Brief explanation of analysis"",
  ""speakerAnalysis"": {{
    ""detectedName"": ""Name if mentioned"",
    ""nameDetected"": false,
    ""speakerGender"": ""male/female/unknown"",
    ""linguisticDNA"": {{
      ""communicationStyle"": ""Formal/Excited/Technical/Passive"",
      ""typicalPhrases"": [""phrase1"", ""phrase2""],
      ""assignedRole"": ""Host/Expert/Questioner/Aggressor"",
      ""sentenceComplexity"": ""Simple/Sophisticated"",
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

**3. Intent & Assistance:**
- Identify when a person is directly asking the AI for help vs just talking to the room.

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

