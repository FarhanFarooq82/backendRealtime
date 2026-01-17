namespace A3ITranslator.Application.DTOs.Frontend;

/// <summary>
/// Conversation item for frontend display
/// Contains essential conversation data with proper language names and confidence metrics
/// </summary>
public class FrontendConversationItem
{
    /// <summary>
    /// Unique identifier for this conversation turn
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when this conversation item was created
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Speaker's display name
    /// </summary>
    public string SpeakerName { get; set; } = string.Empty;

    /// <summary>
    /// Speaker identification confidence (0.0 - 1.0)
    /// </summary>
    public float SpeakerConfidence { get; set; }

    /// <summary>
    /// Original transcribed text (improved/final version)
    /// </summary>
    public string TranscriptionText { get; set; } = string.Empty;

    /// <summary>
    /// Source language name (not BCP-47 code) - e.g., "English", "Spanish"
    /// </summary>
    public string SourceLanguageName { get; set; } = string.Empty;

    /// <summary>
    /// Transcription confidence from STT service (0.0 - 1.0)
    /// </summary>
    public float TranscriptionConfidence { get; set; }

    /// <summary>
    /// Translated text
    /// </summary>
    public string TranslationText { get; set; } = string.Empty;

    /// <summary>
    /// Target language name (not BCP-47 code) - e.g., "English", "Spanish"
    /// </summary>
    public string TargetLanguageName { get; set; } = string.Empty;

    /// <summary>
    /// Translation confidence from translation service (0.0 - 1.0)
    /// </summary>
    public float TranslationConfidence { get; set; }

    /// <summary>
    /// Type of response (Translation, AIResponse, System)
    /// </summary>
    public string ResponseType { get; set; } = "Translation";

    /// <summary>
    /// Convert BCP-47 language code to readable language name
    /// </summary>
    /// <param name="bcp47Code">BCP-47 language code (e.g., "en-US")</param>
    /// <returns>Human-readable language name (e.g., "English")</returns>
    public static string GetLanguageName(string bcp47Code)
    {
        return bcp47Code.ToLowerInvariant() switch
        {
            "en" or "en-us" or "en-gb" => "English",
            "es" or "es-es" or "es-mx" => "Spanish",
            "fr" or "fr-fr" or "fr-ca" => "French",
            "de" or "de-de" => "German",
            "it" or "it-it" => "Italian",
            "pt" or "pt-br" or "pt-pt" => "Portuguese",
            "ru" or "ru-ru" => "Russian",
            "zh" or "zh-cn" or "zh-tw" => "Chinese",
            "ja" or "ja-jp" => "Japanese",
            "ko" or "ko-kr" => "Korean",
            "ar" or "ar-sa" => "Arabic",
            "hi" or "hi-in" => "Hindi",
            "nl" or "nl-nl" => "Dutch",
            "sv" or "sv-se" => "Swedish",
            "no" or "nb-no" => "Norwegian",
            "da" or "da-dk" => "Danish",
            "fi" or "fi-fi" => "Finnish",
            "pl" or "pl-pl" => "Polish",
            "tr" or "tr-tr" => "Turkish",
            "th" or "th-th" => "Thai",
            "vi" or "vi-vn" => "Vietnamese",
            "he" or "he-il" => "Hebrew",
            "cs" or "cs-cz" => "Czech",
            "sk" or "sk-sk" => "Slovak",
            "hu" or "hu-hu" => "Hungarian",
            "ro" or "ro-ro" => "Romanian",
            "bg" or "bg-bg" => "Bulgarian",
            "hr" or "hr-hr" => "Croatian",
            "sl" or "sl-si" => "Slovenian",
            "et" or "et-ee" => "Estonian",
            "lv" or "lv-lv" => "Latvian",
            "lt" or "lt-lt" => "Lithuanian",
            "el" or "el-gr" => "Greek",
            "uk" or "uk-ua" => "Ukrainian",
            "ur" or "ur-pk" => "Urdu",
            _ => bcp47Code // Fallback to original code if not found
        };
    }
}
