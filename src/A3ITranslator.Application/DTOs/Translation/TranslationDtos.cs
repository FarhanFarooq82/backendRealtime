using System.ComponentModel.DataAnnotations;

namespace A3ITranslator.Application.DTOs.Translation;

/// <summary>
/// Request model for text translation
/// </summary>
public class TranslateTextRequestDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(10000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Source language code (ISO 639-1). If null, auto-detection will be used.
    /// </summary>
    [StringLength(10)]
    public string? SourceLanguage { get; set; }

    /// <summary>
    /// Target language code (ISO 639-1). Defaults to English if not specified.
    /// </summary>
    [StringLength(10)]
    public string? TargetLanguage { get; set; } = "en";

    /// <summary>
    /// Additional context to help with translation accuracy
    /// </summary>
    [StringLength(1000)]
    public string? Context { get; set; }

    /// <summary>
    /// Translation quality level (basic, standard, premium)
    /// </summary>
    [StringLength(20)]
    public string? Quality { get; set; } = "standard";

    /// <summary>
    /// Domain-specific terminology (medical, legal, technical, etc.)
    /// </summary>
    [StringLength(50)]
    public string? Domain { get; set; }
}

/// <summary>
/// Response model for text translation
/// </summary>
public class TranslateTextResponseDto
{
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public double? ConfidenceScore { get; set; }
    public DateTime Timestamp { get; set; }
    public string Service { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Language information for translation support
/// </summary>
public class TranslationLanguageDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public bool IsSupported { get; set; } = true;
    public string[] Regions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Translation service health status
/// </summary>
public class TranslationHealthDto
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public string[] AvailableEndpoints { get; set; } = Array.Empty<string>();
    public Dictionary<string, object>? ServiceMetrics { get; set; }
}
