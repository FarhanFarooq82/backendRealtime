using A3ITranslator.Application.Models;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace A3ITranslator.Infrastructure.Services.Orchestration;

public class TranscriptionManager : ITranscriptionManager
{
    private readonly ILogger<TranscriptionManager> _logger;
    private readonly IStreamingSTTService _sttService;

    public TranscriptionManager(
        ILogger<TranscriptionManager> logger,
        IStreamingSTTService sttService)
    {
        _logger = logger;
        _sttService = sttService;
    }

    public async Task<TranscriptionCompetitionResult> RunCompetitionAsync(
        ChannelReader<byte[]> primaryAudio,
        ChannelReader<byte[]> secondaryAudio,
        string connectionId,
        string primaryLanguage,
        string secondaryLanguage,
        Func<bool> isUtteranceCompleted,
        Func<string, TranscriptionResult, Task> onPartialResult,
        CancellationToken cancellationToken)
    {
        var primaryUtteranceManager = new LanguageSpecificUtteranceManager(primaryLanguage, "Primary");
        var secondaryUtteranceManager = new LanguageSpecificUtteranceManager(secondaryLanguage, "Secondary");

        using var primaryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var secondaryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var primaryTask = ProcessSingleLanguageSTTAsync(primaryAudio, primaryLanguage, primaryUtteranceManager, connectionId, primaryCts.Token);
        var secondaryTask = ProcessSingleLanguageSTTAsync(secondaryAudio, secondaryLanguage, secondaryUtteranceManager, connectionId, secondaryCts.Token);

        var winnerSelected = false;
        LanguageSpecificUtteranceManager? winner = null;
        Task? winnerTask = null;

        // Monitor for winner
        var monitoringTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && !winnerSelected)
            {
                if (isUtteranceCompleted() && !winnerSelected)
                {
                    winner = SelectWinner(primaryUtteranceManager, secondaryUtteranceManager, true);
                    if (winner != null)
                    {
                        winnerSelected = true;
                        if (winner == primaryUtteranceManager) secondaryCts.Cancel();
                        else primaryCts.Cancel();
                        
                        winnerTask = winner == primaryUtteranceManager ? primaryTask : secondaryTask;
                    }
                    break;
                }

                await Task.Delay(100, cancellationToken);

                winner = SelectWinner(primaryUtteranceManager, secondaryUtteranceManager, false);
                if (winner != null)
                {
                    winnerSelected = true;
                    if (winner == primaryUtteranceManager) secondaryCts.Cancel();
                    else primaryCts.Cancel();
                    
                    winnerTask = winner == primaryUtteranceManager ? primaryTask : secondaryTask;
                    
                    // After winner is selected, continue monitoring only the winner
                    await MonitorWinnerAsync(winner, onPartialResult, isUtteranceCompleted, cancellationToken);
                    break;
                }
            }
        }, cancellationToken);

        try
        {
            await Task.WhenAny(primaryTask, secondaryTask, monitoringTask);

            if (isUtteranceCompleted() && !winnerSelected)
            {
                // Wait for tasks to complete (they might be finishing naturally or cancelled)
                await Task.WhenAll(primaryTask, secondaryTask);
                
                winner = SelectWinner(primaryUtteranceManager, secondaryUtteranceManager, true);
                if (winner == null)
                {
                    winner = primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() 
                        ? primaryUtteranceManager 
                        : secondaryUtteranceManager;
                }
                winnerSelected = true;
                winnerTask = winner == primaryUtteranceManager ? primaryTask : secondaryTask;
            }

            if (winnerSelected && winnerTask != null)
            {
                await winnerTask;
            }
        }
        catch (OperationCanceledException)
        {
            primaryCts.Cancel();
            secondaryCts.Cancel();
        }

        // Fallback if no winner selected during monitoring
        winner ??= primaryUtteranceManager.GetConfidence() >= secondaryUtteranceManager.GetConfidence() 
            ? primaryUtteranceManager 
            : secondaryUtteranceManager;

        var loser = winner == primaryUtteranceManager ? secondaryUtteranceManager : primaryUtteranceManager;

        return new TranscriptionCompetitionResult
        {
            WinnerLanguage = winner.LanguageCode,
            WinnerResults = winner.GetAllResults(),
            WinnerBestText = winner.GetBestText(),
            WinnerConfidence = winner.GetConfidence(),
            
            LoserLanguage = loser.LanguageCode,
            LoserResults = loser.GetAllResults(),
            LoserBestText = loser.GetBestText(),
            LoserConfidence = loser.GetConfidence(),
            
            TotalDurationSeconds = winner.GetTotalDurationSeconds()
        };
    }

    private LanguageSpecificUtteranceManager? SelectWinner(
        LanguageSpecificUtteranceManager primary, 
        LanguageSpecificUtteranceManager secondary, 
        bool isFinalCheck)
    {
        var primaryCandidate = primary.IsWinnerCandidate();
        var secondaryCandidate = secondary.IsWinnerCandidate();

        if (primaryCandidate || secondaryCandidate)
        {
            return primaryCandidate && secondaryCandidate 
                ? (primary.GetConfidence() >= secondary.GetConfidence() ? primary : secondary)
                : (primaryCandidate ? primary : secondary);
        }

        if (isFinalCheck)
        {
            return primary.GetConfidence() >= secondary.GetConfidence() ? primary : secondary;
        }

        return null;
    }

    private async Task ProcessSingleLanguageSTTAsync(
        ChannelReader<byte[]> audioReader,
        string language,
        LanguageSpecificUtteranceManager utteranceManager,
        string connectionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var result in _sttService.ProcessStreamAsync(audioReader, language, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;
                utteranceManager.AddResult(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STT error for {Language} on connection {ConnectionId}", language, connectionId);
        }
    }

    private async Task MonitorWinnerAsync(
        LanguageSpecificUtteranceManager winner,
        Func<string, TranscriptionResult, Task> onPartialResult,
        Func<bool> isUtteranceCompleted,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !isUtteranceCompleted())
        {
            await Task.Delay(50, cancellationToken);
            if (winner.HasNewResults())
            {
                foreach (var result in winner.GetAndClearNewResults())
                {
                    await onPartialResult(winner.GetBestText(), result);
                }
            }
        }
    }
}

internal class LanguageSpecificUtteranceManager
{
    public string LanguageCode { get; }
    public string Name { get; }
    
    private readonly List<TranscriptionResult> _allResults = new();
    private readonly List<TranscriptionResult> _newResults = new();
    private readonly List<string> _finalUtterances = new();
    private string _currentInterim = string.Empty;
    private readonly object _lock = new();
    
    private const float WINNER_CONFIDENCE_THRESHOLD = 0.7f;
    private const int WINNER_MIN_TEXT_LENGTH = 10;
    
    public LanguageSpecificUtteranceManager(string languageCode, string name)
    {
        LanguageCode = languageCode;
        Name = name;
    }
    
    public void AddResult(TranscriptionResult result)
    {
        lock (_lock)
        {
            _allResults.Add(result);
            _newResults.Add(result);
            
            if (result.IsFinal && !string.IsNullOrWhiteSpace(result.Text))
            {
                _finalUtterances.Add(result.Text.Trim());
                _currentInterim = string.Empty;
            }
            else
            {
                _currentInterim = result.Text ?? string.Empty;
            }
        }
    }
    
    public bool IsWinnerCandidate()
    {
        lock (_lock)
        {
            var confidence = GetConfidence();
            var text = GetBestText();
            var hasFinalText = _finalUtterances.Any() && _finalUtterances.Sum(u => u.Length) >= WINNER_MIN_TEXT_LENGTH;
            var hasGoodInterim = !string.IsNullOrWhiteSpace(_currentInterim) && 
                                _currentInterim.Length >= WINNER_MIN_TEXT_LENGTH && 
                                confidence >= WINNER_CONFIDENCE_THRESHOLD + 0.1f;
            
            return confidence >= WINNER_CONFIDENCE_THRESHOLD && 
                   text.Length >= WINNER_MIN_TEXT_LENGTH &&
                   (hasFinalText || hasGoodInterim);
        }
    }
    
    public float GetConfidence()
    {
        lock (_lock)
        {
            if (!_allResults.Any()) return 0f;
            return (float)_allResults.Average(r => r.Confidence);
        }
    }
    
    public string GetBestText()
    {
        lock (_lock)
        {
            var accumulated = string.Join(" ", _finalUtterances).Trim();
            if (!string.IsNullOrWhiteSpace(_currentInterim))
            {
                return string.IsNullOrWhiteSpace(accumulated) ? _currentInterim : $"{accumulated} {_currentInterim}";
            }
            return accumulated;
        }
    }
    
    public string GetCurrentInterim()
    {
        lock (_lock) return _currentInterim;
    }

    public double GetTotalDurationSeconds()
    {
        lock (_lock)
        {
            return _allResults.Where(r => r.IsFinal).Sum(r => r.Duration.TotalSeconds);
        }
    }
    
    public List<TranscriptionResult> GetAllResults()
    {
        lock (_lock) return _allResults.ToList();
    }
    
    public bool HasNewResults()
    {
        lock (_lock) return _newResults.Any();
    }
    
    public List<TranscriptionResult> GetAndClearNewResults()
    {
        lock (_lock)
        {
            var results = _newResults.ToList();
            _newResults.Clear();
            return results;
        }
    }
}
