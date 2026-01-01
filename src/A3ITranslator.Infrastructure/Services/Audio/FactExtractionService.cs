using A3ITranslator.Application.Models;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.DTOs.Translation;
using Microsoft.Extensions.Logging;
using SpeakerModel = A3ITranslator.Application.Models.Speaker; 

namespace A3ITranslator.Infrastructure.Services.Audio;

public class FactExtractionService : IFactExtractionService
{
    private readonly ILogger<FactExtractionService> _logger;
    private readonly static Dictionary<string, List<SessionFact>> _sessionFacts = new();

    public FactExtractionService(ILogger<FactExtractionService> logger)
    {
        _logger = logger;
    }

    public Task<FactExtractionResult> ProcessFactExtractionAsync(
        FactExtractionData factExtractionData,
        string sessionId,
        string speakerId,
        string speakerName,
        string sourceLanguage,
        int messageSequence)
    {
        if (factExtractionData == null || factExtractionData.Facts == null)
        {
            return Task.FromResult(new FactExtractionResult { Success = true });
        }

        var newFacts = new List<SessionFact>();
        
        foreach (var extractedFact in factExtractionData.Facts)
        {
             newFacts.Add(new SessionFact 
             {
                 SessionId = sessionId,
                 FactContent = extractedFact.Text, // Map from ExtractedFact
                 SpeakerId = speakerId,
                 SpeakerName = speakerName,
                 ExtractedAt = DateTime.UtcNow,
                 Confidence = extractedFact.Confidence 
             });
        }

        if (!_sessionFacts.ContainsKey(sessionId))
        {
            _sessionFacts[sessionId] = new List<SessionFact>();
        }
        
        _sessionFacts[sessionId].AddRange(newFacts);
        
        return Task.FromResult(new FactExtractionResult 
        { 
            Success = true, 
            FactCount = newFacts.Count,
            ExtractedFacts = newFacts
        });
    }

    public Task<SpeakerModel?> UpdateSpeakerFromFactsAsync(
        string sessionId,
        string speakerId,
        bool genderMismatch,
        string detectedGender)
    {
        return Task.FromResult<SpeakerModel?>(null);
    }

    public Task<List<SpeakerModel>> GetSessionSpeakersAsync(string sessionId)
    {
        return Task.FromResult(new List<SpeakerModel>());
    }

    public Task<string> BuildFactContextAsync(string sessionId, int maxLength = 2000)
    {
        if (!_sessionFacts.TryGetValue(sessionId, out var facts))
        {
            return Task.FromResult(string.Empty);
        }

        var context = string.Join("\n", facts.TakeLast(10).Select(f => $"- {f.FactContent}"));
        return Task.FromResult(context.Length > maxLength ? context.Substring(0, maxLength) : context);
    }
}
