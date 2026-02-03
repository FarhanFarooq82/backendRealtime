# Real-Time Translation System - Complete Architecture Flow

**Version:** 2.0  
**Last Updated:** 2026-02-03  
**Purpose:** Complete end-to-end flow documentation for developers

---

## 📋 Table of Contents

1. [System Overview](#system-overview)
2. [Complete Conversation Cycle](#complete-conversation-cycle)
   - [Phase 1: Audio Analysis](#phase-1-audio-analysis-parallel-tracks)
   - [Phase 2: Translation (Dual GenAI)](#phase-2-dual-genai-tracks-parallel--independent)
   - [Phase 3: AI Assistant Mode](#phase-3-ai-assistant-mode-intent-detection)
3. [Data Structures](#data-structures)
4. [Session Management](#session-management)
5. [Performance Characteristics](#performance-characteristics)

---

## 🎯 System Overview

### Architecture Principles

- **Dual-Track Processing**: Pulse (fast) and Brain (deep) run in parallel
- **Non-Blocking TTS**: Audio plays while refinement continues
- **Sequential STT**: Google primary, Azure fallback (not parallel)
- **Speaker Consistency**: ONNX + Neural Roster ensure voice continuity
- **AI-Driven Summaries**: Native language generation with RTL support

### Key Technologies

- **STT**: Google Cloud Speech-to-Text V2 (primary), Azure Speech (fallback)
- **Speaker Detection**: ONNX model (local inference)
- **Translation**: Gemini Flash 1.5 (Pulse), Gemini Pro 1.5 (Brain)
- **TTS**: Azure Neural Voices with speaker assignment
- **Communication**: SignalR WebSockets
- **Summary**: Parallel bilingual generation with AI-native headings

---

## 🔄 Complete Conversation Cycle

### Phase 1: Audio Analysis (Parallel Tracks)

```
Frontend Audio (WebM Opus)
        │
        └─→ SignalR: Hub.ReceiveAudioAsync()
                    │
        ┌───────────▼────────────────────────────┐
        │ ConversationOrchestrator               │
        │ HandleIncomingAudioAsync()             │
        └───────────┬────────────────────────────┘
                    │
                    │ ✨ IMMEDIATE FAN-OUT (PARALLEL!)
                    │
        ┌───────────┴────────────────┐
        │                            │
        │                            │
┌───────▼───────────────┐    ┌───────▼────────────────────┐
│ STT ORCHESTRATOR      │    │ ONNX SPEAKER DETECTION     │
│ (Text Extraction)     │    │ (Local ML Inference)       │
│                       │    │                            │
│ ┌─────────────────┐  │    │ ┌────────────────────────┐ │
│ │ Google STT V2   │  │    │ │ Audio Features (MFCC)  │ │
│ │ (Primary)       │  │    │ │         ↓              │ │
│ │                 │  │    │ │ Speaker Embedding      │ │
│ │ Per language:   │  │    │ │         ↓              │ │
│ │ - ur-PK: 0.95 ✓ │  │    │ │ Returns:               │ │
│ │ - da-DK: 0.42   │  │    │ │ - Provisional ID       │ │
│ │ - en-US: 0.23   │  │    │ │   "PROV_SPK_A"         │ │
│ │                 │  │    │ │ - Gender: "Male"       │ │
│ │ Winner: ur-PK   │  │    │ │ - Speaker Confidence   │ │
│ │ Text: "یہ..."   │  │    │ │   0.78                 │ │
│ │                 │  │    │ └────────────────────────┘ │
│ │ If FAIL:        │  │    │                            │
│ │   ↓ Azure STT   │  │    │ ≈ 200ms (very fast)       │
│ │   (Fallback)    │  │    │                            │
│ └─────────────────┘  │    └────────────────────────────┘
│                      │                 ↓
│ ≈ 500ms             │
└──────┬───────────────┘
       │
       │ Results merge at 500ms
       └───────────┬──────────────┘
                   │
                   ▼
            Merged Results:
            {
              text: "یہ ٹیسٹ...",
              detectedLanguage: "ur-PK",
              provisionalSpeaker: "PROV_SPK_A",
              speakerConfidence: 0.78,
              gender: "Male"
            }
```

**Key Points:**

- **STT is SEQUENTIAL**: Google first, Azure only on failure
- **ONNX is PARALLEL**: Runs simultaneously with STT
- **Language Detection**: STT tries all configured languages, picks highest confidence
- **Provisional Speaker**: ONNX provides fast speaker ID for TTS

---

### Phase 2: Dual GenAI Tracks (Parallel & Independent)

```
                   │ Merged STT + ONNX Results
                   │
    ┌──────────────▼────────────────────────┐
    │ Build Enhanced Translation Request    │
    │ - Text: "یہ ٹیسٹ..."                  │
    │ - Detected language: ur-PK            │
    │ - Target language: da-DK              │
    │ - Recent history (5 turns)            │
    │ - Speaker hint: PROV_SPK_A            │
    │ - ONNX confidence: 0.78               │
    └──────────────┬────────────────────────┘
                   │
                   │ ✨ FAN-OUT TO PARALLEL GENAI TRACKS
                   │
    ┌──────────────┴─────────────────┐
    │                                │
    │ (BOTH RUN SIMULTANEOUSLY!)     │
    │                                │
┌───▼──────────────────┐    ┌────────▼──────────────────────┐
│ PULSE TRACK          │    │ BRAIN TRACK                   │
│ (TTS Priority)       │    │ (Deep Analysis)               │
│                      │    │                               │
│ ┌──────────────────┐ │    │ ┌───────────────────────────┐ │
│ │ Gemini Flash 1.5 │ │    │ │ Gemini Pro 1.5            │ │
│ │ (Fastest Model)  │ │    │ │ (Smart Model)             │ │
│ │                  │ │    │ │                           │ │
│ │ Input:           │ │    │ │ Input:                    │ │
│ │ - Text           │ │    │ │ - Text                    │ │
│ │ - Source: ur-PK  │ │    │ │ - Source: ur-PK           │ │
│ │ - Target: da-DK  │ │    │ │ - Target: da-DK           │ │
│ │ - Min context    │ │    │ │ - Full history            │ │
│ │                  │ │    │ │ - Speaker context         │ │
│ │ Returns:         │ │    │ │ - ONNX speaker hint       │ │
│ │ - Translation:   │ │    │ │ - Significant turns       │ │
│ │   "Dette er..."  │ │    │ │                           │ │
│ │ - Gender hint    │ │    │ │ Returns:                  │ │
│ │                  │ │    │ │ - Translation:            │ │
│ │ ≈ 800ms         │ │    │ │   "Dette er faktisk..."   │ │
│ └────────┬─────────┘ │    │ │ - Speaker ID:             │ │
│          │           │    │ │   "ahmed_khan_xyz"        │ │
│          ▼           │    │ │ - Speaker name:           │ │
│ ┌──────────────────┐ │    │ │   "Ahmed Khan"            │ │
│ │ Voice Selector   │ │    │ │ - User confidence: 0.94   │ │
│ │                  │ │    │ │ - Gender/Age metadata     │ │
│ │ Logic:           │ │    │ │ - isSignificant: true     │ │
│ │ 1. Roster check  │ │    │ │ - Decision type: CONFIRM  │ │
│ │    PROV_SPK_A?   │ │    │ │                           │ │
│ │    NO → Create   │ │    │ │ ≈ 2-3 seconds            │ │
│ │                  │ │    │ └───────────┬───────────────┘ │
│ │ 2. Assign voice: │ │    │             │                 │
│ │    - Lang: da-DK │ │    │             ▼                 │
│ │    - Gender: M   │ │    │ ┌───────────────────────────┐ │
│ │    → JeppeNeural │ │    │ │ SpeakerRosterService      │ │
│ │                  │ │    │ │ (Neural Roster Mgmt)      │ │
│ └────────┬─────────┘ │    │ │                           │ │
│          │           │    │ │ Neural Matching:          │ │
│          ▼           │    │ │ 1. Known speaker?         │ │
│ ┌──────────────────┐ │    │ │    "ahmed_khan_xyz"       │ │
│ │ Azure Neural TTS │ │    │ │    → YES: SPK_001         │ │
│ │                  │ │    │ │                           │ │
│ │ Synthesize:      │ │    │ │ 2. Match PROV_SPK_A?      │ │
│ │ - Text: Danish   │ │    │ │    Conf: 0.94 > 0.85      │ │
│ │ - Voice: Jeppe   │ │    │ │    → MERGE & UPGRADE!     │ │
│ │ - Speaker: PROV  │ │    │ │                           │ │
│ │                  │ │    │ │ 3. Update roster:         │ │
│ │ Stream chunks:   │ │    │ │    PROV_SPK_A → SPK_001   │ │
│ │ → Frontend ──────┼─┼─┐  │ │    Keep voice: Jeppe      │ │
│ │ → Frontend ──────┼─┼─┤  │ │                           │ │
│ │ → Frontend ──────┼─┼─┤  │ │ Result: SPK_001           │ │
│ │ → ...            │ │ │  │ │   "Ahmed Khan"            │ │
│ └──────────────────┘ │ │  │ │   JeppeNeural (same!)     │ │
│                      │ │  │ └───────────┬───────────────┘ │
│ ✅ USER HEARS AUDIO  │ │  │             │                 │
│    ~1.5s latency!    │ │  │             ▼                 │
│                      │ │  │ ┌───────────────────────────┐ │
│ ❌ NO ConversationItem│ │  │ │ ConversationTurn          │ │
│    to frontend yet!  │ │  │ │                           │ │
│                      │ │  │ │ Create/Update:            │ │
└──────────────────────┘ │  │ │ {                         │ │
                         │  │ │   SequenceNumber: N,      │ │
                         │  │ │   SpeakerId: "SPK_001",   │ │
                         │  │ │   SpeakerName: "Ahmed",   │ │
                         │  │ │   Language: "ur-PK",      │ │
                         │  │ │   TargetLang: "da-DK",    │ │
                         │  │ │   OriginalText: "یہ...",  │ │
                         │  │ │   TranslatedText: "Dette",│ │
                         │  │ │   TranscriptionConf: 0.78,│ │
                         │  │ │   SpeakerConf: 0.94,      │ │
                         │  │ │   TranslationConf: 0.94,  │ │
                         │  │ │   IsSignificant: true,    │ │
                         │  │ │   Metadata: { ... }       │ │
                         │  │ │ }                         │ │
                         │  │ └───────────┬───────────────┘ │
                         │  │             │                 │
                         │  │             ▼                 │
                         │  │ ┌───────────────────────────┐ │
                         │  │ │ ConversationSession       │ │
                         │  │ │ AddConversationTurn()     │ │
                         │  │ │                           │ │
                         │  │ │ Auto-assign sequence:     │ │
                         │  │ │   turn.SequenceNumber =   │ │
                         │  │ │     history.Count + 1     │ │
                         │  │ │                           │ │
                         │  │ │ Store in repository       │ │
                         │  │ └───────────┬───────────────┘ │
                         │  │             │                 │
                         │  │             ▼                 │
                         │  │ ┌───────────────────────────┐ │
                         │  │ │ Map to Frontend DTO       │ │
                         │  │ │                           │ │
                         │  │ │ FrontendConversationItem: │ │
                         │  │ │ {                         │ │
                         │  │ │   id: "guid",             │ │
                         │  │ │   timestamp: "...",       │ │
                         │  │ │   speakerName: "Ahmed",   │ │
                         │  │ │   speakerConfidence: 0.94,│ │
                         │  │ │   transcriptionText: "یہ",│ │
                         │  │ │   sourceLanguageName:     │ │
                         │  │ │     "Urdu",               │ │
                         │  │ │   transcriptionConf: 0.78,│ │
                         │  │ │   translationText: "Dette"│ │
                         │  │ │   targetLanguageName:     │ │
                         │  │ │     "Danish",             │ │
                         │  │ │   translationConf: 0.94,  │ │
                         │  │ │   responseType: "Trans"   │ │
                         │  │ │ }                         │ │
                         │  │ └───────────┬───────────────┘ │
                         │  │             │                 │
                         │  │             ▼                 │
                         │  │    SignalR: ReceiveFrontend- │
                         │  │    ConversationItem()         │
                         │  │             │                 │
                         │  │             ▼                 │
                         │  │       Frontend Updates UI     │
                         │  │                               │
                         │  └───────────────────────────────┘
                         │
                         └─→ Audio chunks continue streaming...
```

**Key Points:**

- **Pulse Track**: Fast translation → Voice assignment → TTS → Audio streaming
- **Brain Track**: Deep analysis → Speaker ID → Roster update → ConversationItem
- **User Experience**: Hears audio at ~1.5s, sees UI update at ~2.8s
- **No Provisional Item**: Only Brain sends ConversationItem to frontend
- **Voice Continuity**: Provisional speaker upgraded but keeps same voice

---

### Phase 3: AI Assistant Mode (Intent Detection)

When a user asks the AI a question (e.g., "Assistant, what is the capital of Denmark?"), the system switches to AI Assistant mode:

```
User speaks: "Assistant, what is the capital of Denmark?" (in Urdu)
        │
        ▼
STT + ONNX (same as Phase 1)
        │
        ▼
Merged Results:
  text: "اسسٹنٹ، ڈنمارک کا دارالحکومت کیا ہے؟"
  detectedLanguage: "ur-PK"
        │
        ▼
    ┌──────────────────────────────────────────┐
    │ Build Enhanced Translation Request       │
    │ - Text: "اسسٹنٹ، ڈنمارک کا دارالحکومت..." │
    │ - Language: ur-PK                        │
    │ - Target: da-DK                          │
    │ - Context: Recent conversation           │
    └──────────────┬───────────────────────────┘
                   │
                   │ ✨ FAN-OUT TO PARALLEL GENAI
                   │
    ┌──────────────┴────────────────┐
    │                               │
┌───▼──────────────────┐    ┌───────▼──────────────────────┐
│ PULSE TRACK          │    │ BRAIN TRACK                  │
│                      │    │                              │
│ ┌──────────────────┐ │    │ ┌──────────────────────────┐ │
│ │ Gemini Flash 1.5 │ │    │ │ Gemini Pro 1.5           │ │
│ │                  │ │    │ │                          │ │
│ │ Detects:         │ │    │ │ Detects:                 │ │
│ │ Intent:          │ │    │ │ Intent:                  │ │
│ │ "AI_ASSISTANCE" ✅│ │    │ │ "AI_ASSISTANCE" ✅       │ │
│ │                  │ │    │ │                          │ │
│ │ Returns:         │ │    │ │ Generates:               │ │
│ │ {                │ │    │ │ {                        │ │
│ │   intent:        │ │    │ │   intent:                │ │
│ │   "AI_ASSISTANCE"│ │    │ │   "AI_ASSISTANCE",       │ │
│ │ }                │ │    │ │   aiAssistance: {        │ │
│ │                  │ │    │ │     response: (ur-PK)    │ │
│ │ ❌ NO TTS!       │ │    │ │     "ڈنمارک کا دارالحکومت│ │
│ │ (Pulse skips     │ │    │ │      کوپن ہیگن ہے۔"     │ │
│ │  audio for AI!)  │ │    │ │     responseTranslated:  │ │
│ │                  │ │    │ │     (da-DK)              │ │
│ │ But sends:       │ │    │ │     "Hovedstaden i       │ │
│ │ "Thinking..." 💭 │ │    │ │      Danmark er          │ │
│ │ to frontend      │ │    │ │      København."         │ │
│ │                  │ │    │ │   },                     │ │
│ │ ≈ 800ms         │ │    │ │   speakerId: "ai-asst",  │ │
│ └──────────────────┘ │    │ │   translation: "" (empty)│ │
│                      │    │ │ }                        │ │
│ ✅ USER SEES:        │    │ │                          │ │
│ "🤖 Assistant is     │    │ │ ≈ 3-4 seconds           │ │
│  thinking..."        │    │ └──────────┬───────────────┘ │
│                      │    │            │                 │
└──────────────────────┘    │            ▼                 │
                            │ ┌──────────────────────────┐ │
                            │ │ TTS ONLY FROM BRAIN!     │ │
                            │ │ (AI Response → Audio)    │ │
                            │ │                          │ │
                            │ │ Text: AI response in     │ │
                            │ │       ORIGINAL audio lang│ │
                            │ │       (Urdu)             │ │
                            │ │                          │ │
                            │ │ "ڈنمارک کا دارالحکومت    │ │
                            │ │  کوپن ہیگن ہے۔"         │ │
                            │ │                          │ │
                            │ │ Language: ur-PK          │ │
                            │ │ Voice: AI Assistant voice│ │
                            │ │                          │ │
                            │ │ Stream chunks → Frontend │ │
                            │ │ ✅ USER HEARS ANSWER     │ │
                            │ │    in their language!    │ │
                            │ └──────────┬───────────────┘ │
                            │            │                 │
                            │            ▼                 │
                            │ ┌──────────────────────────┐ │
                            │ │ Create ConversationItem  │ │
                            │ │                          │ │
                            │ │ TWO items sent:          │ │
                            │ │                          │ │
                            │ │ 1. User Question:        │ │
                            │ │ {                        │ │
                            │ │   speakerName: "Ahmed",  │ │
                            │ │   transcriptionText:     │ │
                            │ │     "اسسٹنٹ، ڈنمارک..."   │ │
                            │ │   sourceLanguageName:    │ │
                            │ │     "Urdu",              │ │
                            │ │   translationText: "",   │ │
                            │ │   targetLanguageName: "", │ │
                            │ │   responseType:          │ │
                            │ │     "Translation"        │ │
                            │ │ }                        │ │
                            │ │                          │ │
                            │ │ 2. AI Answer:            │ │
                            │ │ {                        │ │
                            │ │   speakerName:           │ │
                            │ │     "AI Assistant",      │ │
                            │ │   transcriptionText:     │ │
                            │ │     "ڈنمارک کا دارالحکومت │ │
                            │ │      کوپن ہیگن ہے۔",    │ │
                            │ │   sourceLanguageName:    │ │
                            │ │     "Urdu",              │ │
                            │ │   translationText:       │ │
                            │ │     "Hovedstaden i       │ │
                            │ │      Danmark er          │ │
                            │ │      København.",        │ │
                            │ │   targetLanguageName:    │ │
                            │ │     "Danish",            │ │
                            │ │   responseType:          │ │
                            │ │     "AIResponse" ✨      │ │
                            │ │ }                        │ │
                            │ └──────────┬───────────────┘ │
                            │            │                 │
                            │            ▼                 │
                            │   SignalR: Send both items  │
                            │   → Frontend                │
                            │                             │
                            └─────────────────────────────┘
```

**AI Assistant Key Points:**

- ✅ **Intent Detection**: Both Pulse and Brain detect "AI_ASSISTANCE" intent
- ❌ **Pulse NO TTS**: When intent is AI_ASSISTANCE, Pulse skips translation TTS
- ✅ **Show "Thinking"**: Frontend shows user that AI is processing
- ✅ **Brain Generates Answer**: Full AI response in original audio language (Urdu)
- ✅ **Brain TTS**: AI answer synthesized to speech in user's language
- ✅ **Two ConversationItems**: 
  - User question (responseType: "Translation")
  - AI answer (responseType: "AIResponse")
- ✅ **Bilingual Answer**: AI response shown in both languages

**Example Dialogue:**

```
User (Urdu): "اسسٹنٹ، ڈنمارک کا دارالحکومت کیا ہے؟"
             "Assistant, what is the capital of Denmark?"
             
System: [Detects AI_ASSISTANCE intent]
        [Skips translation TTS from Pulse]
        [Shows "🤖 Assistant is thinking..."]
        
AI (Urdu): "ڈنمارک کا دارالحکومت کوپن ہیگن ہے۔"
           "The capital of Denmark is Copenhagen."

Frontend displays:
┌────────────────────────────────────────┐
│ 👤 Ahmed Khan                          │
│ 🇵🇰 Urdu                               │
│ "اسسٹنٹ، ڈنمارک کا دارالحکومت کیا ہے؟" │
├────────────────────────────────────────┤
│ 🤖 AI Assistant                        │
│ 🇵🇰 Urdu                               │
│ "ڈنمارک کا دارالحکومت کوپن ہیگن ہے۔"  │
│ 🇩🇰 Danish                             │
│ "Hovedstaden i Danmark er København."  │
└────────────────────────────────────────┘
```

---

### Intent-Based Flow Decision

```
Gemini determines Intent:
        │
        ├─→ "SIMPLE_TRANSLATION"
        │   ├─→ Pulse: Generate translation → TTS
        │   └─→ Brain: Refine translation → ConversationItem
        │
        └─→ "AI_ASSISTANCE"
            ├─→ Pulse: Detect intent → Show "Thinking..." → NO TTS
            └─→ Brain: Generate AI answer → TTS (audio language) → TWO ConversationItems
```

**Implementation Files:**

- **Intent Detection**: `TranslationPromptService.cs` (Pulse & Brain prompts)
- **Pulse TTS Skip**: `ConversationResponseService.SendPulseAudioOnlyAsync()` 
  ```csharp
  if (pulseResponse.Intent == "SIMPLE_TRANSLATION" && !string.IsNullOrEmpty(pulseResponse.Translation))
  {
      await SendToTTSContinuousAsync(...); // Only for translations!
  }
  ```
- **Brain TTS**: `ConversationResponseService.ProcessAndNotifyAsync()`
  ```csharp
  bool shouldStreamTTS = !translationResponse.IsPulse && translationResponse.Intent == "AI_ASSISTANCE";
  if (shouldStreamTTS) {
      string tts Text = translationResponse.AIAssistance.Response; // Original language!
      string ttsLanguage = translationResponse.AudioLanguage;
  }
  ```
- **AI Item Creation**: `FrontendConversationItemService.CreateFromAIResponse()`



### Timing Diagram

```
Time: 0ms ─────────────────────────────────────→ 3000ms

Audio Arrives
    │
    ├─→ STT (500ms) ──────────────┐
    ├─→ ONNX (200ms) ──────┐      │
    │                      ▼      ▼
    │                  Merge (500ms)
    │                      │
    │    ┌─────────────────┴──────────────────┐
    │    │                                    │
    │ ┌──▼────────────┐            ┌──────────▼─────────┐
    │ │ PULSE TRACK   │            │ BRAIN TRACK        │
    │ │               │            │                    │
    │ │ Flash (800ms) │            │ Pro (2000ms)       │
    │ │      ↓        │            │      ↓             │
    │ │ Voice (100ms) │            │ Speaker ID (300ms) │
    │ │      ↓        │            │      ↓             │
    │ │ TTS (600ms)   │            │ Create Turn        │
    │ │      ↓        │            │      ↓             │
    │ │ Audio chunks  │            │ Send Item          │
    │ │ → Frontend    │            │ → Frontend         │
    │ │               │            │                    │
    │ │ ✅ 1500ms     │            │ ✅ 2800ms          │
    │ └───────────────┘            └────────────────────┘
    │
    │ USER EXPERIENCE:
    │   ~1.5s: Audio starts playing (Pulse complete)
    │   ~2.8s: UI updates with refined data (Brain complete)
```

---

## 📊 Data Structures

### Backend: ConversationTurn (Internal Storage)

```csharp
public class ConversationTurn
{
    // Ordering & Identification
    public int SequenceNumber { get; set; }        // Auto-assigned: count + 1
    public string TurnId { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Speaker Information
    public string SpeakerId { get; set; }          // "SPK_001"
    public string SpeakerName { get; set; }        // "Ahmed Khan"
    public float SpeakerConfidence { get; set; }   // 0.94 (from Brain)
    
    // Transcription
    public string Language { get; set; }           // "ur-PK" (BCP-47)
    public string OriginalText { get; set; }       // "یہ ایک ٹیسٹ ہے"
    public float TranscriptionConfidence { get; set; } // 0.78 (from ONNX)
    
    // Translation
    public string? TranslatedText { get; set; }    // "Dette er faktisk..."
    public float TranslationConfidence { get; set; } // 0.94 (from Brain)
    
    // Backend-Only Flags
    public bool IsSignificant { get; set; }        // For context building only!
    
    // Metadata (not sent to frontend)
    public Dictionary<string, object> Metadata { get; set; }
}
```

**Usage of `IsSignificant`:**

1. **Storage**: Mark important turns in conversation history
2. **Brain Prompts**: Include recent significant points for better context
3. **Summary**: Highlight key decisions in bilingual summaries
4. **NOT sent to frontend**: Backend-only contextualization

---

### Frontend: FrontendConversationItem (Display DTO)

```csharp
public class FrontendConversationItem
{
    // Turn Identification
    public string Id { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Speaker
    public string SpeakerName { get; set; }        // "Ahmed Khan"
    public float SpeakerConfidence { get; set; }   // 0.94 (from Brain)
    
    // Transcription
    public string TranscriptionText { get; set; }  // "یہ ایک ٹیسٹ ہے"
    public string SourceLanguageName { get; set; } // "Urdu" (NOT "ur-PK")
    public float TranscriptionConfidence { get; set; } // 0.78 (from ONNX)
    
    // Translation
    public string TranslationText { get; set; }    // "Dette er faktisk..."
    public string TargetLanguageName { get; set; } // "Danish" (NOT "da-DK")
    public float TranslationConfidence { get; set; } // 0.94 (from Brain)
    
    // Response Type
    public string ResponseType { get; set; }       // "Translation" | "AIResponse"
}
```

**What's Excluded from Frontend:**

- ❌ `metadata` - Backend analytics only
- ❌ `assignedVoice` - Backend TTS routing only
- ❌ `isSignificant` - Backend context only
- ❌ Language codes - Replaced with readable names

---

### Confidence Sources

| Field | Source | Purpose |
|-------|--------|---------|
| **transcriptionConfidence** | ONNX Speaker Detection (0.78) | Audio analysis accuracy |
| **speakerConfidence** | Brain User Detection (0.94) | Speaker identification accuracy |
| **translationConfidence** | Brain Translation (0.94) | Translation quality score |

---

## 🎯 Session Management

### Session Lifecycle

```
1. Session Creation
   Frontend connects → SignalR Hub
   ↓
   ConversationOrchestrator creates ConversationState
   ↓
   Session stored in repository

2. Active Conversation (N turns)
   Each turn:
   - Audio → STT + ONNX (parallel)
   - Pulse + Brain (parallel)
   - TTS streaming (from Pulse)
   - ConversationItem (from Brain)
   - Turn stored with auto-incrementing SequenceNumber

3. Summary Generation (On-Demand)
   User clicks "Generate Summary"
   ↓
   hub.invoke("RequestSummary")
   ↓
   ConversationLifecycleManager.RequestSummaryAsync()
   ↓
   Parallel bilingual summary generation
   ↓
   SessionSummaryDTO sent to frontend

4. Session Finalization
   User clicks "End Session & Email"
   ↓
   hub.invoke("FinalizeAndMail", [emails])
   ↓
   Generate PDF (mock)
   ↓
   Send emails
   ↓
   SendFinalizationSuccessAsync()
   ↓
   Frontend disconnects SignalR
   ↓
   Session cleaned from repository
```

---

### Summary Generation (Parallel Bilingual)

```
User clicks "Generate Summary"
        │
        └─→ hub.invoke("RequestSummary")
                    │
        ┌───────────▼──────────────────────────────┐
        │ ConversationLifecycleManager             │
        │ RequestSummaryAsync()                    │
        │                                          │
        │ 1. Fetch all turns (ordered by Sequence) │
        │ 2. Build single-language contexts        │
        └───────────┬──────────────────────────────┘
                    │
                    │ ✨ 50% Token Reduction!
                    │    (Single language per context)
                    │
        ┌───────────┴──────────────┐
        │                          │
        │ (PARALLEL GENERATION!)   │
        │                          │
    ┌───▼─────────────┐    ┌───────▼──────────┐
    │ Primary Context │    │ Secondary Context│
    │ (Urdu)          │    │ (Danish)         │
    │                 │    │                  │
    │ For each turn:  │    │ For each turn:   │
    │   If lang=ur-PK │    │   If lang=da-DK  │
    │     Use Original│    │     Use Original │
    │   Else          │    │   Else           │
    │     Use Trans   │    │     Use Trans    │
    │                 │    │                  │
    │ Result:         │    │ Result:          │
    │ "مرحبا..."      │    │ "Hej..."         │
    │ "یہ ٹیسٹ..."    │    │ "Dette er..."    │
    └─────┬───────────┘    └───────┬──────────┘
          │                        │
    ┌─────▼──────────────┐  ┌──────▼─────────────┐
    │ Gemini Flash 1.5   │  │ Gemini Flash 1.5   │
    │                    │  │                    │
    │ GenerateSummary    │  │ GenerateSummary    │
    │ InLanguageAsync    │  │ InLanguageAsync    │
    │ ("ur-PK")          │  │ ("da-DK")          │
    │                    │  │                    │
    │ AI Prompt:         │  │ AI Prompt:         │
    │ "Generate summary  │  │ "Generate summary  │
    │  entirely in اردو  │  │  entirely in Dansk │
    │  with NATIVE       │  │  with NATIVE       │
    │  culturally-       │  │  culturally-       │
    │  appropriate       │  │  appropriate       │
    │  headings!"        │  │  headings!"        │
    │                    │  │                    │
    │ AI Generates:      │  │ AI Generates:      │
    │ **تاریخ**: ...    │  │ **Dato**: ...      │
    │ **مقصد**: ...     │  │ **Formål**: ...    │
    │ **شرکاء**: ...    │  │ **Deltagere**: ... │
    │                    │  │                    │
    │ ≈ 5 seconds       │  │ ≈ 5 seconds       │
    └─────┬──────────────┘  └──────┬─────────────┘
          │                        │
          │  Both complete ~5s     │
          └──────────┬─────────────┘
                     │
        ┌────────────▼─────────────────────────────┐
        │ SessionSummaryDTO                        │
        │ {                                        │
        │   primary: {                             │
        │     language: "ur-PK",                   │
        │     languageName: "اردو",                │
        │     isRTL: true,                         │
        │     content: "**تاریخ**: ..."            │
        │   },                                     │
        │   secondary: {                           │
        │     language: "da-DK",                   │
        │     languageName: "Dansk",               │
        │     isRTL: false,                        │
        │     content: "**Dato**: ..."             │
        │   },                                     │
        │   generatedAt: "2026-02-03T13:00:00Z",   │
        │   totalTurns: 720,                       │
        │   meetingDuration: "04:00:00"            │
        │ }                                        │
        └────────────┬─────────────────────────────┘
                     │
                     │ SignalR: ReceiveStructuredSummary()
                     ▼
                  Frontend
```

**Summary Features:**

- ✅ **AI-Native Headings**: No hardcoded dictionaries, AI generates culturally appropriate section names
- ✅ **Parallel Generation**: Primary and secondary summaries generated simultaneously
- ✅ **RTL Support**: `isRTL` flag for proper text direction rendering
- ✅ **Token Efficient**: Single-language contexts reduce token usage by 50%
- ✅ **Universal Language Support**: Works with any language pair

---

## ⚡ Performance Characteristics

### Latency Breakdown

| Operation | Time | Blocking? | Notes |
|-----------|------|-----------|-------|
| **STT (Google)** | 500ms | Yes | Per-language trials, winner selection |
| **STT (Azure)** | 600ms | Yes | Only on Google failure |
| **ONNX Speaker** | 200ms | No | Parallel with STT |
| **Merge Results** | <10ms | Yes | Combine STT + ONNX |
| **Pulse Translation** | 800ms | No | Parallel with Brain |
| **Brain Translation** | 2-3s | No | Deep analysis |
| **Voice Selection** | 100ms | No | Roster lookup |
| **TTS Synthesis** | 600ms | No | Streaming chunks |
| **Audio to Frontend** | ~1.5s | No | User hears voice |
| **UI Update** | ~2.8s | No | ConversationItem displayed |
| **Summary (single)** | 5s | Yes | Per language |
| **Summary (both)** | 5s | Yes | Parallel generation |

### Parallel Operations

```
🔀 PARALLEL TRACK 1: STT + ONNX
   Duration: max(500ms, 200ms) = 500ms

🔀 PARALLEL TRACK 2: Pulse + Brain
   Duration: Both run simultaneously
   User Impact: min(1.5s Pulse, 2.8s Brain) = 1.5s to hear audio

🔀 PARALLEL TRACK 3: Summary Generation
   Duration: max(5s primary, 5s secondary) = 5s total
```

### Token Optimization

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Summary Input Tokens** | 115k | 58k | **50% reduction** |
| **Method** | Bilingual context | Single-language contexts | Per-language filtering |
| **API Calls** | 1 combined | 2 parallel | Same latency |

---

## 🎨 Frontend Integration Guide

### SignalR Event Handlers

```typescript
// Audio streaming (from Pulse)
connection.on("ReceiveFrontendTTSChunk", (chunk: FrontendTTSChunk) => {
  // Play audio immediately
  audioPlayer.enqueue(chunk.audioData);
});

// Conversation updates (from Brain)
connection.on("ReceiveFrontendConversationItem", (item: FrontendConversationItem) => {
  // Update UI with refined speaker and translation
  conversationStore.addOrUpdate(item);
});

// Summary (on-demand)
connection.on("ReceiveStructuredSummary", (summary: SessionSummaryDTO) => {
  // Display bilingual summary with RTL support
  summaryView.render(summary);
});

// Session end
connection.on("ReceiveFinalizationSuccess", () => {
  // Show success, disconnect
  showNotification("Email sent!");
  connection.stop();
});
```

### TypeScript Interfaces

```typescript
interface FrontendConversationItem {
  id: string;
  timestamp: string;
  speakerName: string;
  speakerConfidence: number;
  transcriptionText: string;
  sourceLanguageName: string;
  transcriptionConfidence: number;
  translationText: string;
  targetLanguageName: string;
  translationConfidence: number;
  responseType: "Translation" | "AIResponse" | "System";
}

interface SessionSummaryDTO {
  primary: SummarySection;
  secondary: SummarySection;
  generatedAt: string;
  totalTurns: number;
  meetingDuration: string;
}

interface SummarySection {
  language: string;        // BCP-47: "ur-PK"
  languageName: string;    // Native: "اردو"
  isRTL: boolean;          // For RTL rendering
  content: string;         // Markdown summary
}
```

---

## 🔧 Developer Notes

### Key Architecture Decisions

1. **STT is Sequential, Not Parallel**
   - Google Cloud STT V2 is primary (supports WebM natively)
   - Azure STT is fallback only (requires PCM conversion)
   - Reason: Avoid duplicate processing costs

2. **ConversationItem Only from Brain**
   - Pulse track focuses on fast TTS
   - Brain track handles complete data enrichment
   - Frontend receives single, complete update

3. **Provisional Speaker Strategy**
   - ONNX provides fast speaker ID for TTS voice assignment
   - Brain later confirms/upgrades speaker with neural matching
   - Voice assignment remains consistent through upgrade

4. **isSignificant is Backend-Only**
   - Used for Brain prompt contextualization
   - Helps highlight important turns in summaries
   - NOT exposed to frontend to avoid UI complexity

5. **Language Names vs Codes**
   - Frontend receives human-readable names ("Urdu", not "ur-PK")
   - Backend uses BCP-47 codes for API calls
   - Conversion happens in DTO mapping layer

### Testing Considerations

- **STT Fallback**: Test Google failure scenarios
- **Speaker Merging**: Test provisional → confirmed upgrades
- **RTL Rendering**: Test with Arabic, Urdu, Hebrew summaries
- **Long Sessions**: Test 4-hour meetings (720+ turns)
- **Parallel Timing**: Verify Pulse completes before Brain
- **Voice Consistency**: Verify same speaker keeps same voice across turns

---

## 📚 Related Documentation

- **Model Configuration**: See `LanguageConfigurationService.cs` for RTL language detection
- **Prompt Engineering**: See `TranslationPromptService.cs` for Brain/Pulse prompt templates
- **Speaker Management**: See `SpeakerRosterService.cs` for neural matching algorithm
- **Voice Assignment**: See `AzureSpeakerVoiceAssignmentService.cs` for voice pool logic

---

**Last Updated:** 2026-02-03  
**Maintained By:** Development Team  
**Questions?** Review code comments or consult the team lead.
