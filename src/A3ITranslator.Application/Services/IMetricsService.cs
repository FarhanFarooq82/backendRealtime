using System;

namespace A3ITranslator.Application.Services;

public enum ServiceCategory
{
    STT,
    Translation,
    TTS,
    Summarization,
    SpeakerID
}

public class UsageMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SessionId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public ServiceCategory Category { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long InputUnits { get; set; }
    public string InputUnitType { get; set; } = string.Empty; // e.g., "Tokens", "Seconds", "Characters"
    public long OutputUnits { get; set; }
    public string OutputUnitType { get; set; } = string.Empty;
    public double AudioLengthSec { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public double CostUSD { get; set; }
    public long LatencyMs { get; set; }
    public string Status { get; set; } = "Success";
    public string ErrorMessage { get; set; } = string.Empty;
}

public interface IMetricsService
{
    Task LogMetricsAsync(UsageMetrics metrics);
}
