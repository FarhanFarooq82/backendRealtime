# Migration Progress: Phase 1 (Domain Layer)

## ✅ Completed
We have established the core Domain Layer within `A3ITranslator.Application`.

### **New Structure Created**
```text
src/A3ITranslator.Application/Domain/
├── Entities/
│   ├── ConversationSession.cs       (Aggregate Root)
│   ├── ConversationTurn.cs          (Entity)
│   ├── SessionStatistics.cs         (Entity)
│   └── Speaker.cs                   (Entity)
├── ValueObjects/
│   ├── SpeakingPatterns.cs
│   └── VoiceCharacteristics.cs
└── Enums/
    └── TurnType.cs
```

### **Changes Made**
1.  **`Speaker`**: Promoted from simple DTO to Domain Entity.
2.  **`ConversationSession`**: Absorbed `SpeakerRegistry` logic to become a true Aggregate Root.
3.  **Namespace**: All new files use `A3ITranslator.Application.Domain.*`.

---

## 🚧 Pending (Next Steps)

### **Phase 2: Application Layer (Features)**
- Create `src/A3ITranslator.Application/Features`.
- Implement `ProcessAudioChunkCommand` using new entities.
- Implement `CommitUtteranceCommand` using new entities.

### **Phase 3: Infrastructure Layer**
- Implement `IUtteranceRepository`.
- Connect to Database (SQL/EF Core).

### **Phase 4: Cleanup**
- Update `RealtimeAudioOrchestrator` to use new entities (or replace with Handlers).
- Delete old files in `src/A3ITranslator.Application/Models`.
