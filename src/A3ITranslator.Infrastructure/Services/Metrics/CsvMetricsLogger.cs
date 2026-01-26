using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure.Services.Metrics;

public class CsvMetricsLogger : IMetricsService
{
    private readonly string _usageLogPath;
    private readonly string _promptLogPath;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
    private readonly ILogger<CsvMetricsLogger> _logger;

    public CsvMetricsLogger(ILogger<CsvMetricsLogger> logger)
    {
        _logger = logger;
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        _usageLogPath = Path.Combine(baseDir, "usage_metrics.csv");
        _promptLogPath = Path.Combine(baseDir, "prompt_history.csv");
        InitializeLogFiles();
    }

    private void InitializeLogFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_usageLogPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            // Initialize Usage Metrics File
            if (!File.Exists(_usageLogPath))
            {
                var header = "Timestamp,SessionId,ConnectionId,Category,Provider,Operation,Model,InputUnits,InputUnitType,OutputUnits,OutputUnitType,AudioLengthSec,CostUSD,LatencyMs,Status,ErrorMessage";
                File.WriteAllText(_usageLogPath, header + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine($"✅ METRICS: Created usage log at: {_usageLogPath}");
            }

            // Initialize Prompt History File
            if (!File.Exists(_promptLogPath))
            {
                var header = "Timestamp,SessionId,Category,Operation,SystemPrompt,UserPrompt,Response";
                File.WriteAllText(_promptLogPath, header + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine($"✅ METRICS: Created prompt history log at: {_promptLogPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize metrics log files");
        }
    }

    public async Task LogMetricsAsync(UsageMetrics metrics)
    {
        await _fileLock.WaitAsync();
        try
        {
            // 1. Append to Usage Metrics (Numbers/Metadata)
            var usageLine = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11:F4},{12:F6},{13},{14},{15}",
                metrics.Timestamp,
                EscapeCsv(metrics.SessionId),
                EscapeCsv(metrics.ConnectionId),
                metrics.Category,
                EscapeCsv(metrics.Provider),
                EscapeCsv(metrics.Operation),
                EscapeCsv(metrics.Model),
                metrics.InputUnits,
                EscapeCsv(metrics.InputUnitType),
                metrics.OutputUnits,
                EscapeCsv(metrics.OutputUnitType),
                metrics.AudioLengthSec,
                metrics.CostUSD,
                metrics.LatencyMs,
                metrics.Status,
                EscapeCsv(metrics.ErrorMessage));

            await File.AppendAllTextAsync(_usageLogPath, usageLine + Environment.NewLine, Encoding.UTF8);

            // 2. Append to Prompt History (Text Data)
            // Only log if there is actually some text data to store
            if (!string.IsNullOrWhiteSpace(metrics.UserPrompt) || !string.IsNullOrWhiteSpace(metrics.Response) || !string.IsNullOrWhiteSpace(metrics.SystemPrompt))
            {
                var promptLine = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6}",
                    metrics.Timestamp,
                    EscapeCsv(metrics.SessionId),
                    metrics.Category,
                    EscapeCsv(metrics.Operation),
                    EscapeCsv(metrics.SystemPrompt),
                    EscapeCsv(metrics.UserPrompt),
                    EscapeCsv(metrics.Response));

                await File.AppendAllTextAsync(_promptLogPath, promptLine + Environment.NewLine, Encoding.UTF8);
            }

            Console.WriteLine($"✅ METRICS LOGGED: {metrics.Category} - {metrics.Operation}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write metrics to CSV");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        
        // Remove literal newlines to keep CSV structure, replace with a space or marker
        var cleaned = value.Replace("\r", " ").Replace("\n", " ");
        
        if (cleaned.Contains(",") || cleaned.Contains("\""))
        {
            return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
        }
        return cleaned;
    }
}
