using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Models.Conversation;

/// <summary>
/// Enhanced utterance with language resolution and speaker context
/// </summary>
public class UtteranceWithContext
{
    public string Text { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string DominantLanguage { get; set; } = string.Empty;
    public float TranscriptionConfidence { get; set; } = 0f;
    public string? ProvisionalSpeakerId { get; set; }
    public float SpeakerConfidence { get; set; } = 0f;
    public List<TranscriptionResult> DetectionResults { get; set; } = new();
    public AudioFingerprint? AudioFingerprint { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Multi-language speaker-aware utterance collector with VAD support
/// Extends the existing pattern with speaker context and language resolution
/// </summary>
public class MultiLanguageSpeakerAwareUtteranceCollector
{
    // ✅ Existing VAD logic
    private readonly List<string> _finalUtterances = new();
    private DateTime _lastResultTime = DateTime.UtcNow;
    private readonly TimeSpan _vadTimeout = TimeSpan.FromMilliseconds(1500);
    private string _currentInterimText = string.Empty;
    
    // ✨ NEW: Enhanced speaker and language tracking
    private readonly Dictionary<string, int> _languageVotes = new();
    private readonly List<TranscriptionResult> _allResults = new();
    private readonly List<float> _confidenceScores = new();
    private AudioFingerprint? _accumulatedAudioFingerprint;
    private string? _provisionalSpeakerId;
    private float _speakerMatchConfidence = 0f;

    /// <summary>
    /// Add transcription result with enhanced tracking
    /// </summary>
    public void AddResult(TranscriptionResult result)
    {
        _lastResultTime = DateTime.UtcNow;
        _allResults.Add(result);
        _confidenceScores.Add((float)result.Confidence);

        // FILTERABLE: STT result received
        Console.WriteLine($"TIMESTAMP_ADD_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - Text: '{result.Text}' - IsFinal: {result.IsFinal} - Language: {result.Language}");

        if (result.IsFinal)
        {
            // FILTERABLE: Final result processed
            Console.WriteLine($"TIMESTAMP_FINAL_RESULT: {DateTime.UtcNow:HH:mm:ss.fff} - Final text: '{result.Text}' - Language: {result.Language}");
            
            // ✅ Existing: Add final utterance
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                _finalUtterances.Add(result.Text.Trim());
            }
            
            // ✨ NEW: Track language votes for dominance calculation
            _languageVotes[result.Language] = 
                _languageVotes.GetValueOrDefault(result.Language, 0) + 1;
                
            _currentInterimText = string.Empty;
        }
        else
        {
            // ✅ Existing: Update interim text
            _currentInterimText = result.Text;
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
    /// Get utterance with resolved languages and speaker context
    /// </summary>
    public UtteranceWithContext GetUtteranceWithResolvedLanguages(
        string[] candidateLanguages, 
        string sessionPrimaryLanguage)
    {
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
    /// Implements the mapping rules from our architecture discussion
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

    // ✅ Existing methods maintained
    public string GetAccumulatedText()
    {
        return string.Join(" ", _finalUtterances).Trim();
    }

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

    public bool HasTimedOut() => DateTime.UtcNow - _lastResultTime >= _vadTimeout;

    public bool HasAccumulatedText => _finalUtterances.Any();

    public int UtteranceCount => _finalUtterances.Count;

    public void Reset()
    {
        _finalUtterances.Clear();
        _currentInterimText = string.Empty;
        _languageVotes.Clear();
        _allResults.Clear();
        _confidenceScores.Clear();
        _lastResultTime = DateTime.UtcNow;
        _provisionalSpeakerId = null;
        _speakerMatchConfidence = 0f;
        _accumulatedAudioFingerprint = null;
    }
}
