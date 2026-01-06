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
    /// Uses updated Google STT languages from official documentation
    /// </summary>
    [HttpGet]
    public ActionResult<object> GetSupportedLanguages()
    {
        // Use Google languages as primary source (updated from official documentation)
        var googleLanguages = GoogleStreamingSTTService.GoogleSTTLanguages;
        
        // Create language response objects
        var languageList = new List<object>();
        
        foreach (var lang in googleLanguages)
        {
            var (countryCode, flag) = GetCountryInfo(lang.Key);
            
            languageList.Add(new
            {
                code = lang.Key,
                name = lang.Value,
                countryCode = countryCode,
                flag = flag,
                provider = "google"
            });
        }

        // Sort by BCP-47 code
        var sortedLanguages = languageList
            .OrderBy(l => ((dynamic)l).code)
            .ToArray();

        _logger.LogInformation("Languages endpoint called, returning {Count} Google STT languages", 
            sortedLanguages.Length);

        return Ok(new
        {
            languages = sortedLanguages,
            count = sortedLanguages.Length,
            provider = "google",
            service = "realtime-audio",
            lastUpdated = "2025-01-05", // Updated with official Google Cloud documentation
            documentation = "https://cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages"
        });
    }

    /// <summary>
    /// Get language by code
    /// </summary>
    [HttpGet("{code}")]
    public ActionResult<object> GetLanguageByCode(string code)
    {
        // Get Google languages
        var googleLanguages = GoogleStreamingSTTService.GoogleSTTLanguages;
        
        // Find by exact BCP-47 code
        if (googleLanguages.TryGetValue(code, out var languageName))
        {
            var (countryCode, flag) = GetCountryInfo(code);
            
            return Ok(new
            {
                code = code,
                name = languageName,
                countryCode = countryCode,
                flag = flag,
                provider = "google"
            });
        }

        return NotFound(new { error = "Language not found", code = code });
    }

    /// <summary>
    /// Get country code and flag from BCP-47 language code
    /// </summary>
    private (string countryCode, string flag) GetCountryInfo(string bcp47Code)
    {
        // Handle special Google language codes
        var countryCode = bcp47Code switch
        {
            // Chinese variants with special formats
            "cmn-Hans-CN" => "CN",
            "cmn-Hant-TW" => "TW", 
            "yue-Hant-HK" => "HK",
            
            // Spanish Latin American
            "es-419" => "419",
            
            // Arabic pseudo-accents
            "ar-XA" => "XA",
            
            // General Swahili (no country)
            "sw" => "KE", // Default to Kenya for Swahili
            
            // General Somali (no country)
            "so-SO" => "SO",
            
            // Punjabi Gurmukhi (special script indicator)
            "pa-Guru-IN" => "IN",
            
            // Default BCP-47 parsing
            _ => ExtractCountryFromBcp47(bcp47Code)
        };
        
        var flag = GetCountryFlag(countryCode);
        return (countryCode, flag);
    }
    
    /// <summary>
    /// Extract country code from standard BCP-47 format
    /// </summary>
    private string ExtractCountryFromBcp47(string bcp47Code)
    {
        var parts = bcp47Code.Split('-');
        if (parts.Length < 2)
        {
            // No country code, use language-based default
            var (defaultCountry, _) = GetDefaultCountryForLanguage(parts[0]);
            return defaultCountry;
        }

        // Return the last part which should be the country code
        return parts[^1].ToUpperInvariant();
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
            "PH" => "🇵🇭", // Philippines
            "ES" => "🇪🇸", // Spain
            "MX" => "🇲🇽", // Mexico
            "FR" => "🇫🇷", // France
            "BR" => "🇧🇷", // Brazil
            "PT" => "🇵🇹", // Portugal
            "CN" => "🇨🇳", // China
            "TW" => "🇹🇼", // Taiwan
            "HK" => "🇭🇰", // Hong Kong
            "DE" => "🇩🇪", // Germany
            "IT" => "🇮🇹", // Italy
            "JP" => "🇯🇵", // Japan
            "KR" => "🇰🇷", // South Korea
            "RU" => "🇺", // Russia
            "NL" => "🇳🇱", // Netherlands
            "TR" => "�🇷", // Turkey
            "PL" => "�🇵�", // Poland
            "CZ" => "🇨🇿", // Czech Republic
            "SK" => "🇸🇰", // Slovakia
            "HU" => "🇭🇺", // Hungary
            "RO" => "🇷�", // Romania
            "BG" => "🇧🇬", // Bulgaria
            "HR" => "��", // Croatia
            "RS" => "🇷🇸", // Serbia
            "SI" => "🇸�", // Slovenia
            "MK" => "�🇰", // North Macedonia
            "GR" => "🇬🇷", // Greece
            "EE" => "��", // Estonia
            "LV" => "🇱🇻", // Latvia
            "LT" => "��", // Lithuania
            "FI" => "��", // Finland
            "SE" => "��", // Sweden
            "DK" => "��", // Denmark
            "NO" => "��", // Norway
            "IS" => "��", // Iceland
            "VN" => "🇻🇳", // Vietnam
            "TH" => "🇹🇭", // Thailand
            "ID" => "🇮🇩", // Indonesia
            "MY" => "🇲🇾", // Malaysia
            "BD" => "🇧🇩", // Bangladesh
            "PK" => "🇵🇰", // Pakistan
            "NP" => "🇳🇵", // Nepal
            "ZA" => "🇿🇦", // South Africa
            "ET" => "🇪🇹", // Ethiopia
            "AZ" => "🇦🇿", // Azerbaijan
            "BY" => "🇧�", // Belarus
            "BA" => "🇧🇦", // Bosnia and Herzegovina
            "IR" => "🇮🇷", // Iran
            "IE" => "🇮🇪", // Ireland
            "NG" => "🇳🇬", // Nigeria
            "IL" => "🇮🇱", // Israel
            "AM" => "🇦🇲", // Armenia
            "GE" => "🇬🇪", // Georgia
            "KZ" => "🇰🇿", // Kazakhstan
            "KH" => "🇰🇭", // Cambodia
            "KG" => "🇰🇬", // Kyrgyzstan
            "LA" => "🇱🇦", // Laos
            "MN" => "�🇳", // Mongolia
            "MM" => "🇲🇲", // Myanmar
            "SO" => "🇸🇴", // Somalia
            "AL" => "🇦🇱", // Albania
            "KE" => "🇰🇪", // Kenya
            "TJ" => "🇹�", // Tajikistan
            "UA" => "��", // Ukraine
            "UZ" => "🇺🇿", // Uzbekistan
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
            "MR" => "🇲🇷", // Mauritania
            "PS" => "🇵🇸", // Palestine
            "419" => "🌎", // Latin America
            "XA" => "🌐", // Pseudo-Accents
            _ => "🌐" // Default for unknown countries
        };
    }

}
