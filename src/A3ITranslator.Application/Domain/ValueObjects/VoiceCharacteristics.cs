namespace A3ITranslator.Application.Domain.ValueObjects;

public record VoiceCharacteristics(
    float Pitch = 150f, 
    float Energy = 0.5f, 
    string Gender = "Unknown",
    float[]? Formants = null)
{
    public float[] Formants { get; init; } = Formants ?? Array.Empty<float>();
}
