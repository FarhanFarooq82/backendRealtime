# Event Flow Diagram

This diagram visualizes the "Chain of Responsibility" driven by Domain Events in the new architecture.

```mermaid
sequenceDiagram
    autonumber
    
    participant User as Frontend User
    participant Handler1 as CommitHandler
    participant EventBus as MediatR (Event Bus)
    participant Handler2 as TranslationEventHandler
    participant Service1 as GenAI Service
    participant Handler3 as TTSEventHandler
    participant Service2 as TTS Service
    participant Client as Frontend Client

    Note over User, Handler1: PHASE 1: THE TRIGGER
    User->>Handler1: Commit Utterance Command
    Handler1->>Handler1: Create Turn & Clear Buffer
    Handler1->>EventBus: PUBLISH: UtteranceCommitted
    Note right of EventBus: Payload: { Transcript: "Hello", SessionId: "123" }

    Note over EventBus, Service1: PHASE 2: INTELLIGENCE
    EventBus->>Handler2: HANDLE: UtteranceCommitted
    Handler2->>Service1: GenerateResponse("Hello")
    Service1-->>Handler2: "Hola, ¿cómo estás?" (AI Response)
    Handler2->>Handler2: Save "Hola..." as System Turn
    Handler2->>EventBus: PUBLISH: TranslationCompleted
    Note right of EventBus: Payload: { Text: "Hola...", TargetLang: "es" }

    Note over EventBus, Client: PHASE 3: DELIVERY
    EventBus->>Handler3: HANDLE: TranslationCompleted
    Handler3->>Client: Send Subtitle ("Hola...")
    
    loop Stream Audio
        Handler3->>Service2: SynthesizeStream("Hola...")
        Service2-->>Handler3: Audio Chunk (Bytes)
        Handler3->>Client: Send Audio Chunk
    end
    
    Handler3->>Client: Send TransactionComplete
```

## Event Responsibilities

### 1. `UtteranceCommitted`
- **When:** Fired immediately after the user stops speaking and the transcript is finalized.
- **Why:** Signals that there is new input text ready for processing.
- **Who Handles It:** `TranslationEventHandler` (to translate/reply), `FactExtractionHandler` (optional, for memory).

### 2. `TranslationCompleted`
- **When:** Fired after the AI/LLM has successfully generated a response or translation.
- **Why:** Signals that there is output text ready to be spoken.
- **Who Handles It:** `TTSEventHandler` (to generate voice), `NotificationHandler` (to update UI text).
