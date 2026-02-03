using System;

namespace A3ITranslator.Application.DTOs.Summary;

/// <summary>
/// Structured session summary containing bilingual summaries with metadata
/// </summary>
public class SessionSummaryDTO
{
    public SummarySection Primary { get; set; } = new();
    public SummarySection Secondary { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public int TotalTurns { get; set; }
    public TimeSpan MeetingDuration { get; set; }
}

/// <summary>
/// Summary content for a single language with RTL support
/// </summary>
public class SummarySection
{
    /// <summary>
    /// BCP-47 language code (e.g., "ur-PK", "da-DK")
    /// </summary>
    public string Language { get; set; } = string.Empty;
    
    /// <summary>
    /// Native language display name (e.g., "اردو", "Dansk")
    /// </summary>
    public string LanguageName { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this language uses right-to-left text direction
    /// </summary>
    public bool IsRTL { get; set; }
    
    /// <summary>
    /// Full markdown summary with AI-generated native headings
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
