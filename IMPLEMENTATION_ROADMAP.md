# Implementation Roadmap: Clean Architecture Migration

## Overview

This document provides a step-by-step implementation plan to migrate from the current session-based architecture to a clean, event-driven architecture following SOLID principles.

---

## Phase 1: Domain Layer (Week 1-2)

### **Goal:** Create domain entities and business logic

### **Tasks:**

#### 1.1 Create Domain Entities

```bash
# Create directory structure
mkdir -p src/A3ITranslator.Domain/Entities
mkdir -p src/A3ITranslator.Domain/ValueObjects
mkdir -p src/A3ITranslator.Domain/Events
mkdir -p src/A3ITranslator.Domain/Repositories
```

#### 1.2 Implement Utterance Aggregate

**File:** `src/A3ITranslator.Domain/Entities/Utterance.cs`

```csharp
namespace A3ITranslator.Domain.Entities;

public class Utterance : AggregateRoot
{
    public Guid Id { get; private set; }
    public string SessionId { get; private set; }
    public string Transcript { get; private set; }
    public UtteranceStatus Status { get; private set; }
    public List<TranscriptionSegment> Segments { get; private set; }
    public byte[] AudioData { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CommittedAt { get; private set; }
    
    private Utterance() 
    {
        Segments = new List<TranscriptionSegment>();
    }
    
    public static Utterance Create(string sessionId)
    {
        var utterance = new Utterance
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Status = UtteranceStatus.Recording,
            Transcript = string.Empty,
            AudioData = Array.Empty<byte>(),
            CreatedAt = DateTime.UtcNow
        };
        
        utterance.AddDomainEvent(new UtteranceCreated(utterance.Id, sessionId));
        return utterance;
    }
    
    public void AddSegment(TranscriptionSegment segment)
    {
        if (Status != UtteranceStatus.Recording)
            throw new InvalidOperationException("Cannot add segments to non-recording utterance");
            
        Segments.Add(segment);
        Transcript = string.Join(" ", Segments.Select(s => s.Text));
        
        AddDomainEvent(new TranscriptionSegmentAdded(Id, segment));
    }
    
    public void AppendAudio(byte[] audioChunk)
    {
        if (Status != UtteranceStatus.Recording)
            throw new InvalidOperationException("Cannot append audio to non-recording utterance");
            
        var newAudioData = new byte[AudioData.Length + audioChunk.Length];
        Buffer.BlockCopy(AudioData, 0, newAudioData, 0, AudioData.Length);
        Buffer.BlockCopy(audioChunk, 0, newAudioData, AudioData.Length, audioChunk.Length);
        AudioData = newAudioData;
    }
    
    public void Commit()
    {
        if (Status != UtteranceStatus.Recording)
            throw new InvalidOperationException("Can only commit recording utterances");
            
        if (string.IsNullOrWhiteSpace(Transcript))
            throw new InvalidOperationException("Cannot commit empty utterance");
            
        Status = UtteranceStatus.Committed;
        CommittedAt = DateTime.UtcNow;
        
        AddDomainEvent(new UtteranceCommitted(Id, SessionId, Transcript, AudioData));
    }
    
    public void MarkAsProcessing()
    {
        if (Status != UtteranceStatus.Committed)
            throw new InvalidOperationException("Can only process committed utterances");
            
        Status = UtteranceStatus.Processing;
    }
    
    public void MarkAsCompleted()
    {
        if (Status != UtteranceStatus.Processing)
            throw new InvalidOperationException("Can only complete processing utterances");
            
        Status = UtteranceStatus.Completed;
    }
    
    public void MarkAsFailed(string errorMessage)
    {
        Status = UtteranceStatus.Failed;
        AddDomainEvent(new UtteranceProcessingFailed(Id, errorMessage));
    }
}

public enum UtteranceStatus
{
    Recording,      // Audio streaming, STT in progress
    Committed,      // User committed, ready for processing
    Processing,     // GenAI/TTS in progress
    Completed,      // All processing done
    Failed          // Error occurred
}
```

#### 1.3 Create Value Objects

**File:** `src/A3ITranslator.Domain/ValueObjects/TranscriptionSegment.cs`

```csharp
namespace A3ITranslator.Domain.ValueObjects;

public record TranscriptionSegment
{
    public string Text { get; init; }
    public bool IsFinal { get; init; }
    public float Confidence { get; init; }
    public DateTime Timestamp { get; init; }
    public string Language { get; init; }
    
    public TranscriptionSegment(
        string text, 
        bool isFinal, 
        float confidence, 
        string language)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty", nameof(text));
            
        if (confidence < 0 || confidence > 1)
            throw new ArgumentException("Confidence must be between 0 and 1", nameof(confidence));
            
        Text = text;
        IsFinal = isFinal;
        Confidence = confidence;
        Language = language;
        Timestamp = DateTime.UtcNow;
    }
}
```

#### 1.4 Define Domain Events

**File:** `src/A3ITranslator.Domain/Events/UtteranceEvents.cs`

```csharp
namespace A3ITranslator.Domain.Events;

public record UtteranceCreated(Guid UtteranceId, string SessionId) : IDomainEvent;

public record TranscriptionSegmentAdded(
    Guid UtteranceId, 
    TranscriptionSegment Segment
) : IDomainEvent;

public record UtteranceCommitted(
    Guid UtteranceId,
    string SessionId,
    string Transcript,
    byte[] AudioData
) : IDomainEvent;

public record UtteranceProcessingFailed(
    Guid UtteranceId,
    string ErrorMessage
) : IDomainEvent;

public interface IDomainEvent
{
    DateTime OccurredAt => DateTime.UtcNow;
}
```

#### 1.5 Create Repository Interfaces

**File:** `src/A3ITranslator.Domain/Repositories/IUtteranceRepository.cs`

```csharp
namespace A3ITranslator.Domain.Repositories;

public interface IUtteranceRepository
{
    Task<Utterance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Utterance?> GetActiveBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task<List<Utterance>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(Utterance utterance, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

#### 1.6 Create Base Classes

**File:** `src/A3ITranslator.Domain/Common/AggregateRoot.cs`

```csharp
namespace A3ITranslator.Domain.Common;

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();
    
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
    
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

---

## Phase 2: CQRS with MediatR (Week 3-4)

### **Goal:** Implement command/query pattern

### **Tasks:**

#### 2.1 Install MediatR

```bash
cd src/A3ITranslator.Application
dotnet add package MediatR
dotnet add package MediatR.Extensions.Microsoft.DependencyInjection
```

#### 2.2 Create Commands

**File:** `src/A3ITranslator.Application/Commands/CommitUtteranceCommand.cs`

```csharp
namespace A3ITranslator.Application.Commands;

public record CommitUtteranceCommand(
    string ConnectionId,
    string SessionId,
    string Language
) : IRequest<CommitUtteranceResult>;

public record CommitUtteranceResult(
    Guid UtteranceId,
    string Transcript,
    bool Success,
    string? ErrorMessage = null
);
```

**File:** `src/A3ITranslator.Application/Commands/ProcessAudioChunkCommand.cs`

```csharp
namespace A3ITranslator.Application.Commands;

public record ProcessAudioChunkCommand(
    string SessionId,
    byte[] AudioData
) : IRequest<ProcessAudioChunkResult>;

public record ProcessAudioChunkResult(
    bool Success,
    string? ErrorMessage = null
);
```

#### 2.3 Create Command Handlers

**File:** `src/A3ITranslator.Application/Handlers/CommitUtteranceCommandHandler.cs`

```csharp
namespace A3ITranslator.Application.Handlers;

public class CommitUtteranceCommandHandler 
    : IRequestHandler<CommitUtteranceCommand, CommitUtteranceResult>
{
    private readonly IUtteranceRepository _utteranceRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<CommitUtteranceCommandHandler> _logger;
    
    public CommitUtteranceCommandHandler(
        IUtteranceRepository utteranceRepo,
        IMediator mediator,
        ILogger<CommitUtteranceCommandHandler> logger)
    {
        _utteranceRepo = utteranceRepo;
        _mediator = mediator;
        _logger = logger;
    }
    
    public async Task<CommitUtteranceResult> Handle(
        CommitUtteranceCommand command, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get active utterance
            var utterance = await _utteranceRepo.GetActiveBySessionIdAsync(
                command.SessionId, 
                cancellationToken
            );
            
            if (utterance == null)
            {
                return new CommitUtteranceResult(
                    Guid.Empty, 
                    "", 
                    false, 
                    "No active utterance found"
                );
            }
            
            // 2. Commit (domain logic)
            utterance.Commit();
            
            // 3. Save
            await _utteranceRepo.SaveAsync(utterance, cancellationToken);
            
            // 4. Publish domain events
            foreach (var domainEvent in utterance.DomainEvents)
            {
                await _mediator.Publish(domainEvent, cancellationToken);
            }
            
            utterance.ClearDomainEvents();
            
            // 5. Return result
            return new CommitUtteranceResult(
                utterance.Id,
                utterance.Transcript,
                true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit utterance for session {SessionId}", 
                command.SessionId);
            return new CommitUtteranceResult(
                Guid.Empty,
                "",
                false,
                ex.Message
            );
        }
    }
}
```

#### 2.4 Update SignalR Hub

**File:** `src/A3ITranslator.API/Hubs/AudioConversationHub.cs`

```csharp
public class AudioConversationHub : Hub<IAudioClient>
{
    private readonly IMediator _mediator;
    private readonly ISessionManager _sessionManager;
    
    public async Task CommitUtterance()
    {
        try
        {
            var session = _sessionManager.GetSession(Context.ConnectionId);
            
            var command = new CommitUtteranceCommand(
                Context.ConnectionId,
                session.SessionId,
                session.GetEffectiveLanguage()
            );
            
            var result = await _mediator.Send(command);
            
            if (!result.Success)
            {
                await Clients.Caller.ReceiveError(result.ErrorMessage ?? "Commit failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CommitUtterance");
            await Clients.Caller.ReceiveError($"Processing failed: {ex.Message}");
        }
    }
}
```

---

## Phase 3: Database & Persistence (Week 5-6)

### **Goal:** Add database persistence

### **Tasks:**

#### 3.1 Create Database Schema

**File:** `src/A3ITranslator.Infrastructure/Data/Migrations/001_CreateUtterancesTable.sql`

```sql
CREATE TABLE Utterances (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SessionId NVARCHAR(100) NOT NULL,
    Transcript NVARCHAR(MAX) NOT NULL,
    Status INT NOT NULL,
    AudioData VARBINARY(MAX),
    CreatedAt DATETIME2 NOT NULL,
    CommittedAt DATETIME2 NULL,
    
    INDEX IX_Utterances_SessionId (SessionId),
    INDEX IX_Utterances_Status (Status),
    INDEX IX_Utterances_CreatedAt (CreatedAt)
);

CREATE TABLE TranscriptionSegments (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    UtteranceId UNIQUEIDENTIFIER NOT NULL,
    Text NVARCHAR(MAX) NOT NULL,
    IsFinal BIT NOT NULL,
    Confidence FLOAT NOT NULL,
    Language NVARCHAR(10) NOT NULL,
    Timestamp DATETIME2 NOT NULL,
    
    FOREIGN KEY (UtteranceId) REFERENCES Utterances(Id) ON DELETE CASCADE,
    INDEX IX_TranscriptionSegments_UtteranceId (UtteranceId)
);

CREATE TABLE Translations (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    UtteranceId UNIQUEIDENTIFIER NOT NULL,
    SourceText NVARCHAR(MAX) NOT NULL,
    TranslatedText NVARCHAR(MAX) NOT NULL,
    SourceLanguage NVARCHAR(10) NOT NULL,
    TargetLanguage NVARCHAR(10) NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    
    FOREIGN KEY (UtteranceId) REFERENCES Utterances(Id) ON DELETE CASCADE,
    INDEX IX_Translations_UtteranceId (UtteranceId)
);
```

#### 3.2 Implement Repository

**File:** `src/A3ITranslator.Infrastructure/Repositories/UtteranceRepository.cs`

```csharp
namespace A3ITranslator.Infrastructure.Repositories;

public class UtteranceRepository : IUtteranceRepository
{
    private readonly ApplicationDbContext _context;
    
    public UtteranceRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<Utterance?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Utterances
            .Include(u => u.Segments)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }
    
    public async Task<Utterance?> GetActiveBySessionIdAsync(
        string sessionId, 
        CancellationToken ct = default)
    {
        return await _context.Utterances
            .Include(u => u.Segments)
            .Where(u => u.SessionId == sessionId)
            .Where(u => u.Status == UtteranceStatus.Recording)
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }
    
    public async Task SaveAsync(Utterance utterance, CancellationToken ct = default)
    {
        if (_context.Entry(utterance).State == EntityState.Detached)
        {
            _context.Utterances.Add(utterance);
        }
        else
        {
            _context.Utterances.Update(utterance);
        }
        
        await _context.SaveChangesAsync(ct);
    }
}
```

---

## Phase 4: Event-Driven Processing (Week 7-8)

### **Goal:** Implement event handlers for async processing

### **Tasks:**

#### 4.1 Create Event Handlers

**File:** `src/A3ITranslator.Application/EventHandlers/UtteranceCommittedEventHandler.cs`

```csharp
namespace A3ITranslator.Application.EventHandlers;

public class TranslationEventHandler : INotificationHandler<UtteranceCommittedEvent>
{
    private readonly IGenAIService _genAI;
    private readonly IFactService _factService;
    private readonly ITranslationRepository _translationRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<TranslationEventHandler> _logger;
    
    public async Task Handle(
        UtteranceCommittedEvent notification, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Build context
            var context = await _factService.BuildContextAsync(
                notification.SessionId, 
                cancellationToken
            );
            
            // 2. Generate translation
            var translation = await _genAI.GenerateResponseAsync(
                systemPrompt: BuildPrompt(context),
                userMessage: notification.Transcript,
                cancellationToken
            );
            
            // 3. Save
            var translationEntity = new Translation
            {
                Id = Guid.NewGuid(),
                UtteranceId = notification.UtteranceId,
                SourceText = notification.Transcript,
                TranslatedText = translation,
                CreatedAt = DateTime.UtcNow
            };
            
            await _translationRepo.SaveAsync(translationEntity, cancellationToken);
            
            // 4. Emit event for TTS
            await _mediator.Publish(
                new TranslationGeneratedEvent(
                    notification.UtteranceId,
                    notification.SessionId,
                    translation
                ),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for utterance {UtteranceId}", 
                notification.UtteranceId);
        }
    }
}
```

---

## Phase 5: Testing & Cleanup (Week 9-10)

### **Goal:** Test and optimize

### **Tasks:**

1. Write unit tests for domain entities
2. Write integration tests for commands
3. Write integration tests for event handlers
4. Performance testing
5. Remove old session-based code
6. Update documentation

---

## Dependency Injection Setup

**File:** `src/A3ITranslator.API/Program.cs`

```csharp
// Add MediatR
builder.Services.AddMediatR(cfg => 
{
    cfg.RegisterServicesFromAssembly(typeof(CommitUtteranceCommand).Assembly);
});

// Add repositories
builder.Services.AddScoped<IUtteranceRepository, UtteranceRepository>();
builder.Services.AddScoped<ITranslationRepository, TranslationRepository>();

// Add database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

---

## Testing Examples

### **Unit Test: Utterance Domain Logic**

```csharp
public class UtteranceTests
{
    [Fact]
    public void Commit_ShouldChangeStatus_WhenRecording()
    {
        // Arrange
        var utterance = Utterance.Create("session-123");
        utterance.AddSegment(new TranscriptionSegment("Hello", true, 0.95f, "en-US"));
        
        // Act
        utterance.Commit();
        
        // Assert
        Assert.Equal(UtteranceStatus.Committed, utterance.Status);
        Assert.NotNull(utterance.CommittedAt);
        Assert.Contains(utterance.DomainEvents, 
            e => e is UtteranceCommitted);
    }
    
    [Fact]
    public void Commit_ShouldThrow_WhenEmpty()
    {
        // Arrange
        var utterance = Utterance.Create("session-123");
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => utterance.Commit());
    }
}
```

### **Integration Test: Commit Command**

```csharp
public class CommitUtteranceCommandTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Handle_ShouldCommitUtterance_WhenValid()
    {
        // Arrange
        var utterance = Utterance.Create("session-123");
        utterance.AddSegment(new TranscriptionSegment("Test", true, 0.95f, "en-US"));
        await _utteranceRepo.SaveAsync(utterance);
        
        var command = new CommitUtteranceCommand(
            "conn-123",
            "session-123",
            "en-US"
        );
        
        // Act
        var result = await _handler.Handle(command, CancellationToken.None);
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test", result.Transcript);
        
        var saved = await _utteranceRepo.GetByIdAsync(result.UtteranceId);
        Assert.Equal(UtteranceStatus.Committed, saved.Status);
    }
}
```

---

## Summary

This roadmap provides a complete migration path from your current session-based architecture to a clean, event-driven architecture. Each phase builds on the previous one, allowing for incremental development and testing.

**Estimated Timeline:** 10 weeks
**Effort:** 1-2 developers full-time

Would you like me to start implementing any of these phases?
