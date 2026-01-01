using A3ITranslator.Application.DTOs.Translation;

namespace A3ITranslator.Application.Services;

/// <summary>
/// Service for logging and analyzing content filter violations
/// </summary>
public interface IContentFilterViolationLogger
{
    /// <summary>
    /// Log a content filter violation to the database for analysis
    /// </summary>
    Task LogViolationAsync(ContentFilterViolationData violationData);
    
    /// <summary>
    /// Get recent content filter violations for analysis
    /// </summary>
    Task<List<ContentFilterViolationSummary>> GetRecentViolationsAsync(int count = 50);
    
    /// <summary>
    /// Get violation statistics
    /// </summary>
    Task<ContentFilterViolationStats> GetViolationStatsAsync(DateTime? since = null);
}

/// <summary>
/// Data for logging a content filter violation
/// </summary>
public class ContentFilterViolationData
{
    public string RequestId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string? SanitizedText { get; set; }
    public string? SystemPrompt { get; set; }
    public string? UserPrompt { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ServiceName { get; set; }
    public string[]? FilterReasons { get; set; }
    public string[]? RemovedPatterns { get; set; }
    public string? FallbackTranslation { get; set; }
    public string? UserIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool WasResolved { get; set; }
    public Dictionary<string, object>? AdditionalMetadata { get; set; }
}

/// <summary>
/// Summary of a content filter violation
/// </summary>
public class ContentFilterViolationSummary
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string OriginalTextPreview { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public bool WasResolved { get; set; }
    public int RemovedPatternsCount { get; set; }
}

/// <summary>
/// Statistics about content filter violations
/// </summary>
public class ContentFilterViolationStats
{
    public int TotalViolations { get; set; }
    public int ResolvedViolations { get; set; }
    public int UnresolvedViolations { get; set; }
    public Dictionary<string, int> ViolationsByLanguage { get; set; } = new();
    public Dictionary<string, int> ViolationsByService { get; set; } = new();
    public Dictionary<string, int> ViolationsByErrorCode { get; set; } = new();
    public List<string> MostCommonPatterns { get; set; } = new();
}
