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
    
    // Labels (Native Language)
    public string LabelDate { get; set; } = string.Empty;
    public string LabelLocation { get; set; } = string.Empty;
    public string LabelTitle { get; set; } = string.Empty;
    public string LabelObjective { get; set; } = string.Empty;
    public string LabelParticipants { get; set; } = string.Empty;
    public string LabelKeyDiscussionPoints { get; set; } = string.Empty;
    public string LabelActionItems { get; set; } = string.Empty;

    // Content Data
    public string Date { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public List<string> Participants { get; set; } = new();
    public List<string> KeyDiscussionPoints { get; set; } = new();
    public List<string> ActionItems { get; set; } = new();
}
