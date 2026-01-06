# Refined Architecture Strategy (Mapped to Existing Structure)

## 📌 Context
Your current solution structure is:
1. **`A3ITranslator.API`**: Gateway / Presentation (SignalR Hubs)
2. **`A3ITranslator.Application`**: Mix of Domain & Application Logic
3. **`A3ITranslator.Infrastructure`**: Implementation details

## 🏗 strategy: "Folders > Projects"
We will implement Clean Architecture **without** creating new `.csproj` files. We will use namespaces and folders to enforce boundaries within `A3ITranslator.Application`.

---

## 1. `A3ITranslator.Application` (The Core)

This project is doing double duty. We will organize it clearly.

### **Restructure Plan:**

```text
src/A3ITranslator.Application/
├── Domain/                         <-- PURE BUSINESS LOGIC
│   ├── Entities/
│   │   ├── Utterance.cs            (Enriched with logic)
│   │   ├── Speaker.cs
│   │   └── ConversationSession.cs
│   ├── Events/
│   │   ├── UtteranceCommitted.cs
│   │   └── TranscriptionReceived.cs
│   ├── ValueObjects/
│   │   └── TranscriptionSegment.cs
│   └── Interfaces/                 <-- REPOSITORY INTERFACES
│       └── IUtteranceRepository.cs
│
├── Features/                       <-- APPLICATION USE CASES (CQRS)
│   ├── Audio/
│   │   ├── Commands/
│   │   │   └── ProcessAudioChunkCommand.cs
│   │   └── Handlers/
│   │       └── ProcessAudioChunkHandler.cs
│   └── Conversation/
│       ├── Commands/
│       │   └── CommitUtteranceCommand.cs
│       └── Events/
│           └── UtteranceCommittedEventHandler.cs (Triggers AI/TTS)
│
└── Common/                         <-- SHARED UTILS
    └── Behaviors/
        └── LoggingBehavior.cs
```

### **Key Changes:**
1. **Move "Anemic Models"** from `Models/*.cs` to `Domain/Entities/*.cs` and add behavior (methods).
2. **Replace "Orchestrators"** with `Features/**/Handlers`. The Orchestrator class is broken down into small, focused Command Handlers.

---

## 2. `A3ITranslator.Infrastructure` (The Engine)

Implements the interfaces defined in the Application layer.

### **Restructure Plan:**

```text
src/A3ITranslator.Infrastructure/
├── Persistence/                    <-- DATABASE
│   ├── Context/
│   │   └── AppDbContext.cs
│   └── Repositories/
│       └── UtteranceRepository.cs
├── Services/                       <-- EXTERNAL ADAPTERS
│   ├── Speech/
│   │   ├── AzureSttService.cs
│   │   └── GoogleSttService.cs
│   └── AI/
│       └── OpenAIService.cs
└── Messaging/
    └── MediatR/                    <-- IN-PROCESS EVENT BUS
```

---

## 3. `A3ITranslator.API` (The Gateway)

Dumb pipe that forwards SignalR events to the Application layer via Commands.

### **Restructure Plan:**

```text
src/A3ITranslator.API/
├── Hubs/
│   └── AudioConversationHub.cs     <-- NOW VERY THIN
└── Controllers/
    └── SessionController.cs
```

### **Code Example: The New Thin Hub**

```csharp
// The Hub no longer holds logic. It just dispatches Commands.
public class AudioConversationHub : Hub
{
    private readonly IMediator _mediator;

    public async Task SendAudioChunk(string base64Data)
    {
        // Fire and forget (or await if critical)
        await _mediator.Send(new ProcessAudioChunkCommand(Context.ConnectionId, base64Data));
    }

    public async Task CommitUtterance()
    {
        // The Handler will do the DB lookup, state change, and Event publishing
        await _mediator.Send(new CommitUtteranceCommand(Context.ConnectionId));
    }
}
```

---

## 🚀 Migration Steps (Safe Path)

We can migrate **feature by feature** without breaking the whole app.

### **Step 1: Create the Domain Core (No breaking changes)**
1. Create `A3ITranslator.Application/Domain` folder.
2. Create `Utterance` entity (as discussed in previous analysis).
3. Create `IUtteranceRepository` interface.

### **Step 2: Setup Infrastructure (Parallel to existing)**
1. Create `A3ITranslator.Infrastructure/Persistence`.
2. Implement `UtteranceRepository`.

### **Step 3: Migrate "Commit Utterance" Flow (The Big Win)**
1. Install `MediatR` in Application layer.
2. Create `CommitUtteranceCommand` and Handler in Application.
3. Move logic from `RealtimeAudioOrchestrator.CommitAndProcessAsync` to this new Handler.
4. Update Hub to call Command instead of Orchestrator.

### **Step 4: Cleanup**
1. Once all methods are moved from Orchestrator, delete the Orchestrator class.
2. Delete implementation of session-based storage.

---

## ⚠️ Recommendations for "Mix" Project

Since `A3ITranslator.Application` contains both Domain and Application logic:
1. **Strict Namespaces:** Ensure `A3ITranslator.Application.Domain` does **NOT** use `using A3ITranslator.Application.Features`.
   - Domain should not know about Features (Commands).
   - Features depend on Domain.
2. **Folder Separation:** Keep them physically separate in the project structure as shown above.

This aligns perfectly with your observation while giving you the solidity of clean architecture.
