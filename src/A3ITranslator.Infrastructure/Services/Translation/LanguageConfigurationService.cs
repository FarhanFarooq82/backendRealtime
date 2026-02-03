using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace A3ITranslator.Infrastructure.Services.Translation;

/// <summary>
/// Provides language configuration utilities for RTL detection and display names
/// </summary>
public static class LanguageConfigurationService
{
    private static readonly HashSet<string> RTL_LANGUAGES = new()
    {
        "ar", "ar-SA", "ar-EG", "ar-AE", "ar-JO", "ar-KW", "ar-QA",  // Arabic variants
        "he", "he-IL",                                                 // Hebrew
        "ur", "ur-PK", "ur-IN",                                       // Urdu
        "fa", "fa-IR",                                                 // Persian/Farsi
        "yi",                                                          // Yiddish
        "arc"                                                          // Aramaic
    };

    /// <summary>
    /// Determines if a language uses right-to-left text direction
    /// </summary>
    public static bool IsRightToLeft(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) 
            return false;
        
        // Check full code (e.g., "ur-PK")
        if (RTL_LANGUAGES.Contains(languageCode)) 
            return true;
        
        // Check base code (e.g., "ur" from "ur-PK")
        var baseCode = languageCode.Split('-')[0];
        return RTL_LANGUAGES.Contains(baseCode);
    }

    /// <summary>
    /// Gets the native display name for a language using .NET CultureInfo
    /// </summary>
    public static string GetLanguageDisplayName(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) 
            return languageCode ?? "Unknown";

        try
        {
            var culture = new CultureInfo(languageCode);
            return culture.NativeName;
        }
        catch
        {
            return languageCode;
        }
    }
}
