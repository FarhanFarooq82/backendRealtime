using Microsoft.AspNetCore.Mvc;
using A3ITranslator.Infrastructure.Services.Audio;

namespace A3ITranslator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LanguagesController : ControllerBase
{
    private readonly ILogger<LanguagesController> _logger;

    public LanguagesController(ILogger<LanguagesController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get supported languages for the realtime translation service
    /// Combines Azure and Google STT languages, avoiding duplicates by BCP-47 code
    /// </summary>
    [HttpGet]
    public ActionResult<object> GetSupportedLanguages()
    {
        // Get Azure languages (primary source)
        var azureLanguages = AzureStreamingSTTService.AzureSTTLanguages;
        
        // Get Google languages  
        var googleLanguages = GoogleStreamingSTTService.GoogleSTTLanguages;
        
        // Create union avoiding duplicates by exact BCP-47 code
        var languageMap = new Dictionary<string, object>();
        
        // Add Azure languages first (priority)
        foreach (var lang in azureLanguages)
        {
            var (countryCode, flag) = GetCountryInfo(lang.Key);
            
            languageMap[lang.Key] = new
            {
                code = lang.Key,
                name = lang.Value,
                countryCode = countryCode,
                flag = flag,
                provider = "azure"
            };
        }
        
        // Add Google languages if code doesn't already exist
        foreach (var lang in googleLanguages)
        {
            if (!languageMap.ContainsKey(lang.Key))
            {
                var (countryCode, flag) = GetCountryInfo(lang.Key);
                
                languageMap[lang.Key] = new
                {
                    code = lang.Key,
                    name = lang.Value,
                    countryCode = countryCode,
                    flag = flag,
                    provider = "google"
                };
            }
        }

        // Sort by BCP-47 code
        var sortedLanguages = languageMap.Values
            .OrderBy(l => ((dynamic)l).code)
            .ToArray();

        _logger.LogInformation("Languages endpoint called, returning {Count} unique languages (Azure: {AzureCount}, Google: {GoogleCount})", 
            sortedLanguages.Length, azureLanguages.Count, googleLanguages.Count);

        return Ok(new
        {
            languages = sortedLanguages,
            count = sortedLanguages.Length,
            providers = new { azure = azureLanguages.Count, google = googleLanguages.Count },
            service = "realtime-audio"
        });
    }

    /// <summary>
    /// Get language by code
    /// </summary>
    [HttpGet("{code}")]
    public ActionResult<object> GetLanguageByCode(string code)
    {
        // Get all languages first
        var allLanguages = GetSupportedLanguages().Value;
        var languagesObj = (dynamic)allLanguages;
        var languages = languagesObj.languages;
        
        // Find by exact BCP-47 code
        foreach (dynamic lang in languages)
        {
            if (string.Equals(lang.code, code, StringComparison.OrdinalIgnoreCase))
            {
                return Ok(lang);
            }
        }

        return NotFound(new { error = "Language not found", code = code });
    }

    /// <summary>
    /// Get country code and flag from BCP-47 language code
    /// </summary>
    private (string countryCode, string flag) GetCountryInfo(string bcp47Code)
    {
        // Extract country code from BCP-47 format (e.g., "en-US" -> "US")
        var parts = bcp47Code.Split('-');
        if (parts.Length < 2)
        {
            // No country code, use language-based default
            return GetDefaultCountryForLanguage(parts[0]);
        }

        var countryCode = parts[1].ToUpperInvariant();
        var flag = GetCountryFlag(countryCode);
        
        return (countryCode, flag);
    }

    /// <summary>
    /// Get default country for languages without explicit country codes
    /// </summary>
    private (string countryCode, string flag) GetDefaultCountryForLanguage(string languageCode)
    {
        return languageCode.ToLower() switch
        {
            "en" => ("US", "🇺🇸"),
            "es" => ("ES", "🇪🇸"),
            "fr" => ("FR", "🇫🇷"),
            "de" => ("DE", "🇩🇪"),
            "it" => ("IT", "🇮🇹"),
            "pt" => ("PT", "🇵🇹"),
            "nl" => ("NL", "🇳🇱"),
            "ru" => ("RU", "🇷🇺"),
            "ja" => ("JP", "🇯🇵"),
            "ko" => ("KR", "🇰🇷"),
            "zh" => ("CN", "🇨🇳"),
            "ar" => ("SA", "🇸🇦"),
            "hi" => ("IN", "🇮🇳"),
            "ur" => ("PK", "🇵🇰"),
            _ => ("XX", "🌐")
        };
    }

    /// <summary>
    /// Get country flag by ISO country code
    /// </summary>
    private string GetCountryFlag(string countryCode)
    {
        return countryCode.ToUpperInvariant() switch
        {
            "US" => "🇺🇸", // United States
            "GB" => "🇬🇧", // United Kingdom
            "AU" => "🇦🇺", // Australia
            "CA" => "🇨🇦", // Canada
            "IN" => "🇮🇳", // India
            "ES" => "🇪🇸", // Spain
            "MX" => "🇲🇽", // Mexico
            "FR" => "🇫🇷", // France
            "DE" => "🇩🇪", // Germany
            "IT" => "🇮🇹", // Italy
            "JP" => "🇯🇵", // Japan
            "KR" => "🇰🇷", // South Korea
            "BR" => "🇧🇷", // Brazil
            "PT" => "🇵🇹", // Portugal
            "RU" => "🇷🇺", // Russia
            "NL" => "🇳🇱", // Netherlands
            "SE" => "🇸🇪", // Sweden
            "DK" => "🇩🇰", // Denmark
            "NO" => "🇳🇴", // Norway
            "FI" => "🇫🇮", // Finland
            "PL" => "🇵🇱", // Poland
            "CZ" => "🇨🇿", // Czech Republic
            "HU" => "🇭🇺", // Hungary
            "TR" => "🇹🇷", // Turkey
            "TH" => "🇹🇭", // Thailand
            "VN" => "🇻🇳", // Vietnam
            "ID" => "🇮🇩", // Indonesia
            "MY" => "🇲🇾", // Malaysia
            "PK" => "🇵🇰", // Pakistan
            "BD" => "🇧🇩", // Bangladesh
            "CN" => "🇨🇳", // China
            "TW" => "🇹🇼", // Taiwan
            "HK" => "🇭🇰", // Hong Kong
            "SA" => "🇸🇦", // Saudi Arabia
            "EG" => "🇪🇬", // Egypt
            "AE" => "🇦🇪", // UAE
            "QA" => "🇶🇦", // Qatar
            "KW" => "🇰🇼", // Kuwait
            "BH" => "🇧🇭", // Bahrain
            "OM" => "🇴🇲", // Oman
            "JO" => "🇯🇴", // Jordan
            "LB" => "🇱🇧", // Lebanon
            "SY" => "🇸🇾", // Syria
            "IQ" => "🇮🇶", // Iraq
            "YE" => "🇾🇪", // Yemen
            "LY" => "🇱🇾", // Libya
            "TN" => "🇹🇳", // Tunisia
            "DZ" => "🇩🇿", // Algeria
            "MA" => "🇲🇦", // Morocco
            _ => "🌐" // Default for unknown countries
        };
    }

}
