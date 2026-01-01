namespace A3ITranslator.Application.DTOs.Api;

/// <summary>
/// Request DTO for creating a new session
/// </summary>
public class SessionRequest
{
    public string MainLanguage { get; set; } = string.Empty;
    public string OtherLanguage { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
}

/// <summary>
/// Response DTO for session operations
/// </summary>
public class SessionResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MainLanguage { get; set; } = string.Empty;
    public string OtherLanguage { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for session end operation
/// </summary>
public class SessionEndResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}


/// <summary>
/// Conversation entry for sync operations
/// </summary>
public class ConversationEntry
{
    
    public int SequenceNumber { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string TranslationLanguage { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string SpeakerId { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for conversation operations
/// </summary>
public class ConversationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ConversationEntry> Messages { get; set; } = new();
}
