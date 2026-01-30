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
    private readonly string _cycleLogPath;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
    private readonly ILogger<CsvMetricsLogger> _logger;

    public CsvMetricsLogger(ILogger<CsvMetricsLogger> logger)
    {
        _logger = logger;
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        _usageLogPath = Path.Combine(baseDir, "usage_metrics.csv");
        _promptLogPath = Path.Combine(baseDir, "prompt_history.csv");
        _cycleLogPath = Path.Combine(baseDir, "cycle_metrics.csv");
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
                // ✨ REMOVED SystemPrompt from CSV Header
                var header = "Timestamp,SessionId,Category,Operation,UserPrompt,Response";
                File.WriteAllText(_promptLogPath, header + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine($"✅ METRICS: Created prompt history log at: {_promptLogPath}");
            }
            // Initialize Cycle Metrics File
            if (!File.Exists(_cycleLogPath))
            {
                var header = "Timestamp,SessionId,ConnectionId,CycleStartTime,VADTriggerTime,GenAIStartTime,GenAIEndTime,CycleEndTime,AudioDurationSec,STTCost,GenAICost,TTSCost,TotalCost,GenAILatencyMs,ImprovedTranscription,Translation";
                File.WriteAllText(_cycleLogPath, header + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine($"✅ METRICS: Created cycle log at: {_cycleLogPath}");
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
            // ✨ REMOVED SystemPrompt from logging
            if (!string.IsNullOrWhiteSpace(metrics.UserPrompt) || !string.IsNullOrWhiteSpace(metrics.Response))
            {
                var promptLine = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5}",
                    metrics.Timestamp,
                    EscapeCsv(metrics.SessionId),
                    metrics.Category,
                    EscapeCsv(metrics.Operation),
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

    public async Task LogCycleMetricsAsync(CycleMetrics metrics)
    {
        await _fileLock.WaitAsync();
        try
        {
            var line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3:yyyy-MM-dd HH:mm:ss.fff},{4:yyyy-MM-dd HH:mm:ss.fff},{5:yyyy-MM-dd HH:mm:ss.fff},{6:yyyy-MM-dd HH:mm:ss.fff},{7:yyyy-MM-dd HH:mm:ss.fff},{8:F4},{9:F6},{10:F6},{11:F6},{12:F6},{13},{14},{15}",
                metrics.Timestamp,
                EscapeCsv(metrics.SessionId),
                EscapeCsv(metrics.ConnectionId),
                metrics.CycleStartTime,
                metrics.VADTriggerTime,
                metrics.GenAIStartTime,
                metrics.GenAIEndTime,
                metrics.CycleEndTime,
                metrics.AudioDurationSec,
                metrics.STTCost,
                metrics.GenAICost,
                metrics.TTSCost,
                metrics.TotalCost,
                metrics.GenAILatencyMs,
                EscapeCsv(metrics.ImprovedTranscription),
                EscapeCsv(metrics.Translation));

            await File.AppendAllTextAsync(_cycleLogPath, line + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine($"✅ CYCLE LOGGED: {metrics.SessionId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write cycle metrics to CSV");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = value.Replace("\r", " ").Replace("\n", " ");
        if (cleaned.Contains(",") || cleaned.Contains("\""))
        {
            return "\"" + cleaned.Replace("\"", "\"\"") + "\"";
        }
        return cleaned;
    }
}
