using A3ITranslator.Application.DTOs.Audio;

namespace A3ITranslator.Application.Services;

public interface IPitchAnalysisService
{
    Task<PitchAnalysisResult> ExtractPitchCharacteristicsAsync(byte[] audioData, CancellationToken cancellationToken = default);
}
