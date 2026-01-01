using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Services.Audio;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class RealtimeLanguageService : IRealtimeLanguageService
{
    public Task<IEnumerable<SupportedLanguage>> GetSupportedLanguagesAsync()
    {
        // Use the comprehensive Azure list as the primary source of truth
        var languages = AzureStreamingSTTService.AzureSTTLanguages
            .Select(kvp => new SupportedLanguage
            {
                Code = kvp.Key,
                Name = kvp.Value,
                NativeName = kvp.Value // Could be refined if we had native names
            })
            .ToList();

        return Task.FromResult<IEnumerable<SupportedLanguage>>(languages);
    }
}
