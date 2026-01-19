using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Models.Conversation;

/// <summary>
/// Modern frontend VAD-driven utterance collector.
/// Collects transcription results until frontend signals completion.
/// Follows SOLID principles and clean architecture patterns.
/// </summary>
public class UtteranceCollector
{
    // Core collection state
    private readonly List<string> _finalUtterances = new();
    private readonly List<TranscriptionResult> _allResults = new();
    private readonly Dictionary<string, int> _languageVotes = new();
    private readonly List<float> _confidenceScores = new();
    
    // Current processing state
    private string _currentInterimText = string.Empty;
    private bool _isCompleted = false;
    
    // Speaker context
    private string? _provisionalSpeakerId;
    private float _speakerMatchConfidence = 0f;
    private AudioFingerprint? _accumulatedAudioFingerprint;

    /// <summary>
    /// Add transcription result from STT service
    /// </summary>
    public void AddTranscriptionResult(TranscriptionResult result)
    {
        if (_isCompleted)
            return; // Ignore results after completion

        _allResults.Add(result);
        _confidenceScores.Add((float)result.Confidence);

        if (result.IsFinal)
        {
            ProcessFinalResult(result);
        }
        else
        {
            UpdateInterimResult(result);
        }
    }

    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// </summary>
    public void CompleteUtterance()
    {
        _isCompleted = true;
        
        // Add any pending interim text as final
        if (!string.IsNullOrWhiteSpace(_currentInterimText))
        {
            _finalUtterances.Add(_currentInterimText.Trim());
            _currentInterimText = string.Empty;
        }
    }

    /// <summary>
    /// Set speaker identification context
    /// </summary>
    public void SetSpeakerContext(string? speakerId, float confidence, AudioFingerprint? fingerprint = null)
    {
        _provisionalSpeakerId = speakerId;
        _speakerMatchConfidence = confidence;
        _accumulatedAudioFingerprint = fingerprint;
    }

    /// <summary>
    /// Get complete utterance with resolved languages and speaker context
    /// </summary>
    public UtteranceWithContext GetCompleteUtterance(
        string[] candidateLanguages, 
        string sessionPrimaryLanguage)
    {
        if (!_isCompleted || !_finalUtterances.Any())
            throw new InvalidOperationException("Utterance not completed or no content available");

        var dominantLanguage = ResolveDominantLanguage();
        var (sourceLanguage, targetLanguage) = ResolveSourceTargetLanguages(
            dominantLanguage, candidateLanguages, sessionPrimaryLanguage);
        var averageConfidence = CalculateAverageConfidence();

        return new UtteranceWithContext
        {
            Text = GetAccumulatedText(),
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            DominantLanguage = dominantLanguage,
            TranscriptionConfidence = averageConfidence,
            ProvisionalSpeakerId = _provisionalSpeakerId,
            SpeakerConfidence = _speakerMatchConfidence,
            DetectionResults = _allResults.ToList(),
            AudioFingerprint = _accumulatedAudioFingerprint,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get accumulated text (for compatibility with existing code)
    /// </summary>
    public string GetAccumulatedText()
    {
        return string.Join(" ", _finalUtterances).Trim();
    }

    /// <summary>
    /// Get current display text for real-time UI updates
    /// </summary>
    public string GetCurrentDisplayText()
    {
        var accumulated = GetAccumulatedText();
        if (!string.IsNullOrWhiteSpace(_currentInterimText))
        {
            return string.IsNullOrWhiteSpace(accumulated) 
                ? _currentInterimText 
                : $"{accumulated} {_currentInterimText}";
        }
        return accumulated;
    }

    /// <summary>
    /// Check if utterance is completed
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>
    /// Check if has accumulated text
    /// </summary>
    public bool HasAccumulatedText => _finalUtterances.Any();

    /// <summary>
    /// Get utterance count
    /// </summary>
    public int UtteranceCount => _finalUtterances.Count;

    /// <summary>
    /// Reset collector for new utterance
    /// </summary>
    public void Reset()
    {
        _finalUtterances.Clear();
        _currentInterimText = string.Empty;
        _languageVotes.Clear();
        _allResults.Clear();
        _confidenceScores.Clear();
        _provisionalSpeakerId = null;
        _speakerMatchConfidence = 0f;
        _accumulatedAudioFingerprint = null;
        _isCompleted = false;
    }

    // --- Private Methods ---

    /// <summary>
    /// Process final transcription result
    /// </summary>
    private void ProcessFinalResult(TranscriptionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            _finalUtterances.Add(result.Text.Trim());
            TrackLanguageVote(result.Language);
        }
        _currentInterimText = string.Empty;
    }

    /// <summary>
    /// Update interim text for real-time display
    /// </summary>
    private void UpdateInterimResult(TranscriptionResult result)
    {
        _currentInterimText = result.Text ?? string.Empty;
        TrackLanguageVote(result.Language);
    }

    /// <summary>
    /// Track language votes for dominant language resolution
    /// </summary>
    private void TrackLanguageVote(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        _languageVotes[language] = _languageVotes.GetValueOrDefault(language, 0) + 1;
    }

    /// <summary>
    /// Resolve the dominant language based on voting
    /// </summary>
    private string ResolveDominantLanguage()
    {
        if (_languageVotes.Count == 0)
            return _allResults.FirstOrDefault()?.Language ?? "en-US";

        return _languageVotes
            .OrderByDescending(kvp => kvp.Value)
            .First()
            .Key;
    }

    /// <summary>
    /// Resolve source and target languages based on dominant language and candidates
    /// </summary>
    private (string sourceLanguage, string targetLanguage) ResolveSourceTargetLanguages(
        string dominantLanguage, 
        string[] candidateLanguages, 
        string sessionPrimaryLanguage)
    {
        // Rule 1: Dominant language is in candidates
        if (candidateLanguages.Contains(dominantLanguage))
        {
            var otherCandidates = candidateLanguages.Where(c => c != dominantLanguage).ToArray();
            var targetLanguage = otherCandidates.FirstOrDefault() ?? sessionPrimaryLanguage;
            return (dominantLanguage, targetLanguage);
        }

        // Rule 2: Check if secondary detected language is in candidates
        var secondaryLanguage = _languageVotes
            .Where(kvp => kvp.Key != dominantLanguage)
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault()
            .Key;

        if (!string.IsNullOrEmpty(secondaryLanguage) && candidateLanguages.Contains(secondaryLanguage))
        {
            var otherCandidates = candidateLanguages.Where(c => c != secondaryLanguage).ToArray();
            var targetLanguage = otherCandidates.FirstOrDefault() ?? sessionPrimaryLanguage;
            return (secondaryLanguage, targetLanguage);
        }

        // Rule 3: Fallback - use detected as source, session primary as target
        return (dominantLanguage, sessionPrimaryLanguage);
    }

    /// <summary>
    /// Calculate average transcription confidence
    /// </summary>
    private float CalculateAverageConfidence()
    {
        if (_confidenceScores.Count == 0) return 0f;
        return _confidenceScores.Average();
    }
}
