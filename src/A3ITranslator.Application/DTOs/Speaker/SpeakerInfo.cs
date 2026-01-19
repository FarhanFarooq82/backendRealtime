namespace A3ITranslator.Application.DTOs.Speaker;

/// <summary>
/// Basic speaker information for DTOs
/// </summary>
public class SpeakerInfo
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown Speaker";
    public float Confidence { get; set; } = 0f;
    public string? Gender { get; set; }
    public bool IsActive { get; set; } = false;
}

/// <summary>
/// Enhanced speaker information with additional metadata
/// </summary>
public class EnhancedSpeakerInfo
{
    public string SpeakerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Unknown Speaker";
    public float Confidence { get; set; } = 0f;
    public string? Gender { get; set; }
    public bool IsActive { get; set; } = false;
    public string? CommunicationStyle { get; set; }
    public List<string> Languages { get; set; } = new();
    public int TotalUtterances { get; set; } = 0;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
}
