namespace A3ITranslator.Application.DTOs.Speaker;

/// <summary>
/// Speaker list update DTO for real-time notifications
/// </summary>
public class SpeakerListUpdate
{
    public List<SpeakerInfo> Speakers { get; set; } = new();
    public bool HasChanges { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Basic speaker model
/// </summary>
public class Speaker
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = false;
}
