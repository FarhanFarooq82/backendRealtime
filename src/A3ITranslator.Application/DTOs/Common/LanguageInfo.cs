namespace A3ITranslator.Application.DTOs.Common;

/// <summary>
/// Language information DTO (simplified from domain value object)
/// </summary>
public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public bool IsRightToLeft { get; set; }

    public LanguageInfo() { }

    public LanguageInfo(string code, string name, string nativeName, bool isRightToLeft = false)
    {
        Code = code.ToLowerInvariant();
        Name = name;
        NativeName = nativeName;
        IsRightToLeft = isRightToLeft;
    }

    // Common language constants
    public static readonly LanguageInfo English = new("en-us", "English", "English");
    public static readonly LanguageInfo Arabic = new("ar-sa", "Arabic", "العربية", true);
    public static readonly LanguageInfo Urdu = new("ur-pk", "Urdu", "اردو", true);
    public static readonly LanguageInfo Hindi = new("hi-in", "Hindi", "हिन्दी");
    public static readonly LanguageInfo Spanish = new("es-es", "Spanish", "Español");
    public static readonly LanguageInfo French = new("fr-fr", "French", "Français");
}
