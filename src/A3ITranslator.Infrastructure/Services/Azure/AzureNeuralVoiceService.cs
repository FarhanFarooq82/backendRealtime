using A3ITranslator.Application.Models.Speaker;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Azure;

/// <summary>
/// Azure Neural Voice Service with comprehensive voice selection based on language and gender
/// Includes pricing optimization between Neural and Standard voices
/// Updated with latest Azure Cognitive Services voices (2024/2025)
/// </summary>
public class AzureNeuralVoiceService : IStreamingTTSService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<AzureNeuralVoiceService> _logger;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ISpeakerVoiceAssignmentService _voiceAssignmentService;

    public AzureNeuralVoiceService(
        IOptions<ServiceOptions> options, 
        ILogger<AzureNeuralVoiceService> logger,
        IRealtimeNotificationService notificationService,
        ISpeakerVoiceAssignmentService voiceAssignmentService)
    {
        _options = options.Value;
        _logger = logger;
        _notificationService = notificationService;
        _voiceAssignmentService = voiceAssignmentService;
    }

    #region Azure Neural Voice Configuration by Language and Gender

    /// <summary>
    /// Comprehensive Azure Neural Voices by Language and Gender
    /// Updated for 2024/2025 - Latest Azure Cognitive Services voices
    /// Neural voices provide higher quality but cost more than Standard voices
    /// </summary>
    public static readonly Dictionary<string, VoiceConfiguration> AzureNeuralVoices = new()
    {
        // English (United States) - en-US
        ["en-US"] = new VoiceConfiguration
        {
            Language = "en-US",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("en-US-AriaNeural", "Aria", VoiceStyle.Conversational, true), // Most popular
                    new("en-US-JennyNeural", "Jenny", VoiceStyle.Professional, true),
                    new("en-US-MichelleNeural", "Michelle", VoiceStyle.Friendly, true),
                    new("en-US-MonicaNeural", "Monica", VoiceStyle.Calm, true),
                    new("en-US-AshleyNeural", "Ashley", VoiceStyle.Young, true),
                    new("en-US-CoraNeural", "Cora", VoiceStyle.Warm, true),
                    new("en-US-ElizabethNeural", "Elizabeth", VoiceStyle.Mature, true),
                    new("en-US-EmmaNeural", "Emma", VoiceStyle.Cheerful, true),
                    new("en-US-AmberNeural", "Amber", VoiceStyle.Energetic, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("en-US-GuyNeural", "Guy", VoiceStyle.Conversational, true), // Most popular
                    new("en-US-BrandonNeural", "Brandon", VoiceStyle.Professional, true),
                    new("en-US-ChristopherNeural", "Christopher", VoiceStyle.Mature, true),
                    new("en-US-EricNeural", "Eric", VoiceStyle.Friendly, true),
                    new("en-US-JacobNeural", "Jacob", VoiceStyle.Young, true),
                    new("en-US-JasonNeural", "Jason", VoiceStyle.Warm, true),
                    new("en-US-TonyNeural", "Tony", VoiceStyle.Energetic, true),
                    new("en-US-RyanNeural", "Ryan", VoiceStyle.Calm, true),
                    new("en-US-AdamNeural", "Adam", VoiceStyle.Cheerful, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("en-US-AriaRUS", "Aria Standard", VoiceStyle.Standard, false),
                    new("en-US-ZiraRUS", "Zira", VoiceStyle.Standard, false)
                },
                [SpeakerGender.Male] = new()
                {
                    new("en-US-BenjaminRUS", "Benjamin", VoiceStyle.Standard, false),
                    new("en-US-GuyRUS", "Guy Standard", VoiceStyle.Standard, false)
                }
            }
        },

        // English (United Kingdom) - en-GB
        ["en-GB"] = new VoiceConfiguration
        {
            Language = "en-GB",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("en-GB-SoniaNeural", "Sonia", VoiceStyle.Conversational, true),
                    new("en-GB-LibbyNeural", "Libby", VoiceStyle.Professional, true),
                    new("en-GB-MaisieNeural", "Maisie", VoiceStyle.Young, true),
                    new("en-GB-BellaNeural", "Bella", VoiceStyle.Cheerful, true),
                    new("en-GB-AbbiNeural", "Abbi", VoiceStyle.Warm, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("en-GB-RyanNeural", "Ryan", VoiceStyle.Conversational, true),
                    new("en-GB-ThomasNeural", "Thomas", VoiceStyle.Professional, true),
                    new("en-GB-AlfieNeural", "Alfie", VoiceStyle.Young, true),
                    new("en-GB-ElliotNeural", "Elliot", VoiceStyle.Warm, true),
                    new("en-GB-OliverNeural", "Oliver", VoiceStyle.Mature, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("en-GB-Susan", "Susan", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("en-GB-George", "George", VoiceStyle.Standard, false) }
            }
        },

        // English (Australia) - en-AU
        ["en-AU"] = new VoiceConfiguration
        {
            Language = "en-AU",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("en-AU-NatashaNeural", "Natasha", VoiceStyle.Conversational, true),
                    new("en-AU-AnnetteNeural", "Annette", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("en-AU-WilliamNeural", "William", VoiceStyle.Conversational, true),
                    new("en-AU-DarrenNeural", "Darren", VoiceStyle.Professional, true)
                }
            }
        },

        // English (India) - en-IN
        ["en-IN"] = new VoiceConfiguration
        {
            Language = "en-IN",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("en-IN-NeerjaNeural", "Neerja", VoiceStyle.Conversational, true),
                    new("en-IN-AnanyaNeural", "Ananya", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("en-IN-PrabhatNeural", "Prabhat", VoiceStyle.Conversational, true),
                    new("en-IN-MadhurNeural", "Madhur", VoiceStyle.Professional, true)
                }
            }
        },

        // Arabic (Saudi Arabia) - ar-SA  
        ["ar-SA"] = new VoiceConfiguration
        {
            Language = "ar-SA",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ar-SA-ZariyahNeural", "Zariyah", VoiceStyle.Conversational, true),
                    new("ar-SA-AmalNeural", "Amal", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ar-SA-HamedNeural", "Hamed", VoiceStyle.Conversational, true),
                    new("ar-SA-SalmanNeural", "Salman", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("ar-SA-Naayf", "Naayf", VoiceStyle.Standard, false) }
            }
        },

        // Urdu (India) - ur-IN
        ["ur-IN"] = new VoiceConfiguration
        {
            Language = "ur-IN", 
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ur-IN-GulNeural", "Gul", VoiceStyle.Conversational, true),
                    new("ur-IN-UzmaNeural", "Uzma", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ur-IN-SalmanNeural", "Salman", VoiceStyle.Conversational, true),
                    new("ur-IN-AsadNeural", "Asad", VoiceStyle.Professional, true)
                }
            }
        },

        // Urdu (Pakistan) - ur-PK
        ["ur-PK"] = new VoiceConfiguration
        {
            Language = "ur-PK",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ur-PK-UzmaNeural", "Uzma", VoiceStyle.Conversational, true),
                    new("ur-PK-ZainabNeural", "Zainab", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ur-PK-AsadNeural", "Asad", VoiceStyle.Conversational, true),
                    new("ur-PK-ImranNeural", "Imran", VoiceStyle.Professional, true)
                }
            }
        },

        // Spanish (Spain) - es-ES
        ["es-ES"] = new VoiceConfiguration
        {
            Language = "es-ES",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("es-ES-ElviraNeural", "Elvira", VoiceStyle.Conversational, true),
                    new("es-ES-AbrilNeural", "Abril", VoiceStyle.Young, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("es-ES-AlvaroNeural", "Alvaro", VoiceStyle.Conversational, true),
                    new("es-ES-PabloNeural", "Pablo", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("es-ES-Helena", "Helena", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("es-ES-Pablo", "Pablo Standard", VoiceStyle.Standard, false) }
            }
        },

        // Spanish (Mexico) - es-MX
        ["es-MX"] = new VoiceConfiguration
        {
            Language = "es-MX",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("es-MX-DaliaNeural", "Dalia", VoiceStyle.Conversational, true),
                    new("es-MX-LarissaNeural", "Larissa", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("es-MX-JorgeNeural", "Jorge", VoiceStyle.Conversational, true),
                    new("es-MX-GerardoNeural", "Gerardo", VoiceStyle.Professional, true)
                }
            }
        },

        // French (France) - fr-FR
        ["fr-FR"] = new VoiceConfiguration
        {
            Language = "fr-FR",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("fr-FR-DeniseNeural", "Denise", VoiceStyle.Conversational, true),
                    new("fr-FR-CoralieNeural", "Coralie", VoiceStyle.Professional, true),
                    new("fr-FR-JacquelineNeural", "Jacqueline", VoiceStyle.Mature, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("fr-FR-HenriNeural", "Henri", VoiceStyle.Conversational, true),
                    new("fr-FR-ClaudeNeural", "Claude", VoiceStyle.Professional, true),
                    new("fr-FR-YvesNeural", "Yves", VoiceStyle.Warm, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("fr-FR-Julie", "Julie", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("fr-FR-Paul", "Paul", VoiceStyle.Standard, false) }
            }
        },

        // French (Canada) - fr-CA
        ["fr-CA"] = new VoiceConfiguration
        {
            Language = "fr-CA",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("fr-CA-SylvieNeural", "Sylvie", VoiceStyle.Conversational, true),
                    new("fr-CA-JeanNeural", "Jean", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("fr-CA-AntoineNeural", "Antoine", VoiceStyle.Conversational, true),
                    new("fr-CA-JeanNeural", "Jean", VoiceStyle.Professional, true)
                }
            }
        },

        // German (Germany) - de-DE  
        ["de-DE"] = new VoiceConfiguration
        {
            Language = "de-DE",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("de-DE-KatjaNeural", "Katja", VoiceStyle.Conversational, true),
                    new("de-DE-AmalaNeural", "Amala", VoiceStyle.Professional, true),
                    new("de-DE-BirgitNeural", "Birgit", VoiceStyle.Warm, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("de-DE-ConradNeural", "Conrad", VoiceStyle.Conversational, true),
                    new("de-DE-KlausNeural", "Klaus", VoiceStyle.Professional, true),
                    new("de-DE-RalfNeural", "Ralf", VoiceStyle.Mature, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("de-DE-Hedda", "Hedda", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("de-DE-Stefan", "Stefan", VoiceStyle.Standard, false) }
            }
        },

        // Italian (Italy) - it-IT
        ["it-IT"] = new VoiceConfiguration
        {
            Language = "it-IT",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("it-IT-ElsaNeural", "Elsa", VoiceStyle.Conversational, true),
                    new("it-IT-IsabellaNeural", "Isabella", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("it-IT-DiegoNeural", "Diego", VoiceStyle.Conversational, true),
                    new("it-IT-BenignoNeural", "Benigno", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("it-IT-Elsa", "Elsa Standard", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("it-IT-Cosimo", "Cosimo", VoiceStyle.Standard, false) }
            }
        },

        // Portuguese (Brazil) - pt-BR
        ["pt-BR"] = new VoiceConfiguration
        {
            Language = "pt-BR",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("pt-BR-FranciscaNeural", "Francisca", VoiceStyle.Conversational, true),
                    new("pt-BR-ThalitaNeural", "Thalita", VoiceStyle.Young, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("pt-BR-AntonioNeural", "Antonio", VoiceStyle.Conversational, true),
                    new("pt-BR-FabioNeural", "Fabio", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("pt-BR-Francisca", "Francisca Standard", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("pt-BR-Daniel", "Daniel", VoiceStyle.Standard, false) }
            }
        },

        // Chinese (Simplified, China) - zh-CN
        ["zh-CN"] = new VoiceConfiguration
        {
            Language = "zh-CN",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("zh-CN-XiaoxiaoNeural", "Xiaoxiao", VoiceStyle.Conversational, true),
                    new("zh-CN-XiaoyouNeural", "Xiaoyou", VoiceStyle.Young, true),
                    new("zh-CN-XiaomengNeural", "Xiaomeng", VoiceStyle.Cheerful, true),
                    new("zh-CN-XiaoyanNeural", "Xiaoyan", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("zh-CN-YunxiNeural", "Yunxi", VoiceStyle.Conversational, true),
                    new("zh-CN-YunjianNeural", "Yunjian", VoiceStyle.Professional, true),
                    new("zh-CN-YunzeNeural", "Yunze", VoiceStyle.Mature, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("zh-CN-Yaoyao", "Yaoyao", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("zh-CN-Kangkang", "Kangkang", VoiceStyle.Standard, false) }
            }
        },

        // Japanese (Japan) - ja-JP
        ["ja-JP"] = new VoiceConfiguration
        {
            Language = "ja-JP",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ja-JP-NanamiNeural", "Nanami", VoiceStyle.Conversational, true),
                    new("ja-JP-AoiNeural", "Aoi", VoiceStyle.Young, true),
                    new("ja-JP-ShioriNeural", "Shiori", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ja-JP-KeitaNeural", "Keita", VoiceStyle.Conversational, true),
                    new("ja-JP-DaichiNeural", "Daichi", VoiceStyle.Professional, true),
                    new("ja-JP-NaokiNeural", "Naoki", VoiceStyle.Mature, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("ja-JP-Ayumi", "Ayumi", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("ja-JP-Ichiro", "Ichiro", VoiceStyle.Standard, false) }
            }
        },

        // Korean (Korea) - ko-KR
        ["ko-KR"] = new VoiceConfiguration
        {
            Language = "ko-KR",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ko-KR-SunHiNeural", "SunHi", VoiceStyle.Conversational, true),
                    new("ko-KR-JiMinNeural", "JiMin", VoiceStyle.Young, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ko-KR-InJoonNeural", "InJoon", VoiceStyle.Conversational, true),
                    new("ko-KR-BongJinNeural", "BongJin", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("ko-KR-HeamiRUS", "Heami", VoiceStyle.Standard, false) }
            }
        },

        // Hindi (India) - hi-IN
        ["hi-IN"] = new VoiceConfiguration
        {
            Language = "hi-IN",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("hi-IN-SwaraNeural", "Swara", VoiceStyle.Conversational, true),
                    new("hi-IN-AnanyaNeural", "Ananya", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("hi-IN-MadhurNeural", "Madhur", VoiceStyle.Conversational, true),
                    new("hi-IN-ArjunNeural", "Arjun", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("hi-IN-Kalpana", "Kalpana", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("hi-IN-Hemant", "Hemant", VoiceStyle.Standard, false) }
            }
        },

        // Russian (Russia) - ru-RU
        ["ru-RU"] = new VoiceConfiguration
        {
            Language = "ru-RU",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("ru-RU-SvetlanaNeural", "Svetlana", VoiceStyle.Conversational, true),
                    new("ru-RU-DariyaNeural", "Dariya", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("ru-RU-DmitryNeural", "Dmitry", VoiceStyle.Conversational, true),
                    new("ru-RU-AleksandrNeural", "Aleksandr", VoiceStyle.Professional, true)
                }
            },
            StandardVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new() { new("ru-RU-Irina", "Irina", VoiceStyle.Standard, false) },
                [SpeakerGender.Male] = new() { new("ru-RU-Pavel", "Pavel", VoiceStyle.Standard, false) }
            }
        },

        // Danish (Denmark) - da-DK
        ["da-DK"] = new VoiceConfiguration
        {
            Language = "da-DK",
            NeuralVoices = new Dictionary<SpeakerGender, List<VoiceOption>>
            {
                [SpeakerGender.Female] = new()
                {
                    new("da-DK-ChristelNeural", "Christel", VoiceStyle.Conversational, true),
                    new("da-DK-TineNeural", "Tine", VoiceStyle.Professional, true)
                },
                [SpeakerGender.Male] = new()
                {
                    new("da-DK-JeppeNeural", "Jeppe", VoiceStyle.Conversational, true),
                    new("da-DK-MadsNeural", "Mads", VoiceStyle.Professional, true)
                }
            }
        }

        // Add more languages as needed...
    };

    #endregion

    #region Voice Selection Logic

    /// <summary>
    /// Select the best voice based on language, gender, and cost preferences
    /// </summary>
    /// <param name="language">Target language (e.g., "en-US")</param>
    /// <param name="speakerGender">Detected speaker gender</param>
    /// <param name="isPremium">Whether to use premium neural voices (higher cost but better quality). Default: false for cost optimization</param>
    /// <param name="voiceStyle">Optional preferred voice style</param>
    /// <returns>Selected voice name or null if not found</returns>
    public static string? SelectOptimalVoice(
        string language, 
        SpeakerGender speakerGender, 
        bool isPremium = false,
        VoiceStyle? voiceStyle = null)
    {
        // Normalize language code
        var langCode = NormalizeLanguageCode(language);
        
        if (!AzureNeuralVoices.TryGetValue(langCode, out var voiceConfig))
        {
            // Fallback to en-US if language not supported
            langCode = "en-US";
            voiceConfig = AzureNeuralVoices[langCode];
        }

        // Try to match gender, fallback to opposite gender if needed
        var gendersToTry = speakerGender == SpeakerGender.Unknown 
            ? new[] { SpeakerGender.Female, SpeakerGender.Male }
            : new[] { speakerGender, speakerGender == SpeakerGender.Male ? SpeakerGender.Female : SpeakerGender.Male };

        foreach (var gender in gendersToTry)
        {
            // Try Premium Neural voices first if requested
            if (isPremium && voiceConfig.NeuralVoices.TryGetValue(gender, out var neuralVoices))
            {
                var selectedVoice = SelectVoiceByStyle(neuralVoices, voiceStyle);
                if (selectedVoice != null) return selectedVoice.VoiceName;
            }

            // Try Standard voices (default for cost optimization)
            if (voiceConfig.StandardVoices.TryGetValue(gender, out var standardVoices))
            {
                var selectedVoice = SelectVoiceByStyle(standardVoices, voiceStyle);
                if (selectedVoice != null) return selectedVoice.VoiceName;
            }

            // If no standard voices available and premium not requested, try neural as fallback
            if (!isPremium && voiceConfig.NeuralVoices.TryGetValue(gender, out var fallbackNeuralVoices))
            {
                var selectedVoice = SelectVoiceByStyle(fallbackNeuralVoices, voiceStyle);
                if (selectedVoice != null) return selectedVoice.VoiceName;
            }
        }

        // Ultimate fallback - return first available neural voice
        var firstNeuralVoice = voiceConfig.NeuralVoices.Values.FirstOrDefault()?.FirstOrDefault();
        if (firstNeuralVoice != null) return firstNeuralVoice.VoiceName;

        // Ultimate ultimate fallback - return first available standard voice  
        var firstStandardVoice = voiceConfig.StandardVoices.Values.FirstOrDefault()?.FirstOrDefault();
        return firstStandardVoice?.VoiceName;
    }

    private static VoiceOption? SelectVoiceByStyle(List<VoiceOption> voices, VoiceStyle? preferredStyle)
    {
        if (preferredStyle.HasValue)
        {
            var matchingVoice = voices.FirstOrDefault(v => v.Style == preferredStyle.Value);
            if (matchingVoice != null) return matchingVoice;
        }

        // Return first conversational voice, or just first voice
        return voices.FirstOrDefault(v => v.Style == VoiceStyle.Conversational) ?? voices.FirstOrDefault();
    }

    private static string NormalizeLanguageCode(string language)
    {
        if (string.IsNullOrEmpty(language)) return "en-US";

        // Handle common variations and normalize to Azure format
        return language.ToLowerInvariant() switch
        {
            "en" or "english" or "en-us" => "en-US",
            "en-gb" or "british" => "en-GB",
            "ar" or "arabic" or "ar-sa" => "ar-SA", 
            "ur" or "urdu" or "ur-in" => "ur-IN",
            "ur-pk" => "ur-PK",
            "es" or "spanish" or "es-es" or "es-mx" => "es-ES",
            "fr" or "french" or "fr-fr" => "fr-FR",
            "de" or "german" or "de-de" => "de-DE",
            "it" or "italian" or "it-it" => "it-IT",
            "pt" or "portuguese" or "pt-br" => "pt-BR",
            "zh" or "chinese" or "zh-cn" => "zh-CN",
            "ja" or "japanese" or "ja-jp" => "ja-JP",
            "ko" or "korean" or "ko-kr" => "ko-KR",
            "hi" or "hindi" or "hi-in" => "hi-IN",
            "ru" or "russian" or "ru-ru" => "ru-RU",
            "da" or "danish" or "da-dk" => "da-DK",
            _ => language // Fallback: hope it matches or let fallback logic handle it
        };
    }

    /// <summary>
    /// Get detailed pricing and availability information for a language
    /// </summary>
    public static VoiceAvailabilityInfo GetVoiceAvailability(string language)
    {
        var langCode = NormalizeLanguageCode(language);
        
        if (!AzureNeuralVoices.TryGetValue(langCode, out var voiceConfig))
        {
            return new VoiceAvailabilityInfo
            {
                Language = langCode,
                IsSupported = false,
                FallbackLanguage = "en-US"
            };
        }

        var neuralCount = voiceConfig.NeuralVoices.Values.SelectMany(v => v).Count();
        var standardCount = voiceConfig.StandardVoices.Values.SelectMany(v => v).Count();

        return new VoiceAvailabilityInfo
        {
            Language = langCode,
            IsSupported = true,
            NeuralVoiceCount = neuralCount,
            StandardVoiceCount = standardCount,
            HasMaleVoices = voiceConfig.NeuralVoices.ContainsKey(SpeakerGender.Male) || voiceConfig.StandardVoices.ContainsKey(SpeakerGender.Male),
            HasFemaleVoices = voiceConfig.NeuralVoices.ContainsKey(SpeakerGender.Female) || voiceConfig.StandardVoices.ContainsKey(SpeakerGender.Female),
            RecommendedVoice = SelectOptimalVoice(langCode, SpeakerGender.Female, isPremium: false), // Default to standard for cost optimization
            PricingTier = neuralCount > 0 ? "Neural + Standard" : "Standard Only"
        };
    }

    #endregion

    #region TTS Service Implementation

    public async Task<string> SynthesizeAndNotifyAsync(
        string connectionId, 
        string text, 
        string language, 
        string? speakerId = null,
        string estimatedGender = "Unknown", 
        bool isPremium = true, 
        CancellationToken cancellationToken = default)
    {
        string selectedVoice;
        
        // ✅ Use speaker-based voice assignment if speakerId is provided
        if (!string.IsNullOrEmpty(speakerId))
        {
            selectedVoice = _voiceAssignmentService.GetOrAssignVoice(speakerId, language, estimatedGender);
            _logger.LogDebug("🎤 Using assigned voice {Voice} for speaker {SpeakerId}", selectedVoice, speakerId);
        }
        else
        {
            // Fallback to legacy gender-based selection
            var gender = estimatedGender.ToLowerInvariant() switch
            {
                "male" => SpeakerGender.Male,
                "female" => SpeakerGender.Female,
                _ => SpeakerGender.Unknown
            };
            selectedVoice = SelectOptimalVoice(language, gender, isPremium, VoiceStyle.Conversational) ?? "en-US-JennyNeural";
            _logger.LogDebug("🎤 Using gender-based voice selection: {Voice}", selectedVoice);
        }

        await foreach (var chunk in SynthesizeStreamAsync(text, language, selectedVoice, cancellationToken))
        {
            await _notificationService.SendTTSAudioSegmentAsync(connectionId, new Application.DTOs.Common.TTSAudioSegment
            {
                AudioData = chunk.AudioData,
                AssociatedText = chunk.AssociatedText,
                IsFirstChunk = chunk.IsFirstChunk,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                ConversationItemId = "neural-tts-" + Guid.NewGuid().ToString()[..8]
            });
        }
        return selectedVoice;
    }

    /// <summary>
    /// Enhanced TTS synthesis with automatic neural voice selection based on gender
    /// </summary>
    public async IAsyncEnumerable<TTSChunk> SynthesizeStreamAsync(
        string text, 
        string language, 
        string voiceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var config = SpeechConfig.FromSubscription(_options.Azure.SpeechKey, _options.Azure.SpeechRegion);
        config.SpeechSynthesisLanguage = NormalizeLanguageCode(language);

        // Auto-select voice if not specified
        if (string.IsNullOrEmpty(voiceName))
        {
            var selectedVoice = SelectOptimalVoice(language, SpeakerGender.Female, isPremium: true);
            if (selectedVoice != null)
            {
                config.SpeechSynthesisVoiceName = selectedVoice;
            }
        }
        else
        {
            config.SpeechSynthesisVoiceName = voiceName;
        }

        // 🚀 QUALITY UPGRADE: Use 160kbps for crystal clear neural audio
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

        using var synthesizer = new SpeechSynthesizer(config, null);
        
        // 🚀 TRUE STREAMING: Start synthesis and get the stream immediately
        using var result = await synthesizer.StartSpeakingTextAsync(text);
        
        if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            _logger.LogError("❌ TTS Synthesis Cancelled: {Reason} - {Details}", cancellation.Reason, cancellation.ErrorDetails);
            yield break;
        }

        using var stream = AudioDataStream.FromResult(result);
        
        byte[] buffer = new byte[8192]; // 8KB chunks for smooth playback
        int totalBytesRead = 0;
        int chunkIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = stream.ReadData(buffer);
            if (bytesRead == 0) break;

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            totalBytesRead += (int)bytesRead;

            yield return new TTSChunk
            {
                AudioData = chunk,
                IsFirstChunk = chunkIndex == 0,
                ChunkIndex = chunkIndex++,
                AssociatedText = text,
                BoundaryType = "chunk"
            };
        }

        // Final completion chunk
        if (!cancellationToken.IsCancellationRequested)
        {
            yield return new TTSChunk
            {
                AudioData = Array.Empty<byte>(),
                IsFirstChunk = false,
                ChunkIndex = chunkIndex,
                AssociatedText = text,
                BoundaryType = "end",
                TotalChunks = chunkIndex
            };
        }

        _logger.LogDebug("✅ True Streaming completed: {Bytes} bytes in {Chunks} chunks", totalBytesRead, chunkIndex);
    }

    /// <summary>
    /// Enhanced synthesis with explicit gender and voice style selection
    /// </summary>
    public async IAsyncEnumerable<TTSChunk> SynthesizeWithGenderAsync(
        string text,
        string language,
        SpeakerGender speakerGender,
        VoiceStyle voiceStyle = VoiceStyle.Conversational,
        bool isPremium = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var selectedVoice = SelectOptimalVoice(language, speakerGender, isPremium, voiceStyle);
        
        _logger.LogInformation("🎭 Selected voice: {Voice} for {Gender} {Language} speaker (Premium: {IsPremium})", 
            selectedVoice, speakerGender, language, isPremium);

        await foreach (var chunk in SynthesizeStreamAsync(text, language, selectedVoice ?? "", cancellationToken))
        {
            yield return chunk;
        }
    }

    #endregion
}

#region Supporting Types

public class VoiceConfiguration
{
    public string Language { get; set; } = string.Empty;
    public Dictionary<SpeakerGender, List<VoiceOption>> NeuralVoices { get; set; } = new();
    public Dictionary<SpeakerGender, List<VoiceOption>> StandardVoices { get; set; } = new();
}

public class VoiceOption
{
    public string VoiceName { get; set; }
    public string DisplayName { get; set; }
    public VoiceStyle Style { get; set; }
    public bool IsNeural { get; set; }

    public VoiceOption(string voiceName, string displayName, VoiceStyle style, bool isNeural)
    {
        VoiceName = voiceName;
        DisplayName = displayName;
        Style = style;
        IsNeural = isNeural;
    }
}

public enum VoiceStyle
{
    Conversational,
    Professional,
    Friendly,
    Warm,
    Calm,
    Young,
    Mature,
    Cheerful,
    Energetic,
    Standard
}

public class VoiceAvailabilityInfo
{
    public string Language { get; set; } = string.Empty;
    public bool IsSupported { get; set; }
    public int NeuralVoiceCount { get; set; }
    public int StandardVoiceCount { get; set; }
    public bool HasMaleVoices { get; set; }
    public bool HasFemaleVoices { get; set; }
    public string? RecommendedVoice { get; set; }
    public string? FallbackLanguage { get; set; }
    public string PricingTier { get; set; } = string.Empty;
}

/// <summary>
/// Azure Cognitive Services TTS Pricing Information (2024/2025)
/// Neural voices: $16 per 1M characters
/// Standard voices: $4 per 1M characters  
/// Neural voices provide significantly better quality and more natural speech
/// Standard voices are more cost-effective but lower quality
/// </summary>
public static class AzureTTSPricing
{
    public const decimal NeuralVoicePricePerMillionChars = 16.00m;
    public const decimal StandardVoicePricePerMillionChars = 4.00m;
    
    public static decimal CalculateCost(int characterCount, bool isNeural)
    {
        var pricePerChar = isNeural ? NeuralVoicePricePerMillionChars / 1_000_000 : StandardVoicePricePerMillionChars / 1_000_000;
        return characterCount * pricePerChar;
    }
    
    public static string GetPricingRecommendation(int averageMonthlyCharacters)
    {
        var neuralCost = CalculateCost(averageMonthlyCharacters, true);
        var standardCost = CalculateCost(averageMonthlyCharacters, false);
        
        return $"Monthly cost estimate: Neural ${neuralCost:F2}, Standard ${standardCost:F2}. " +
               $"Neural voices cost {NeuralVoicePricePerMillionChars/StandardVoicePricePerMillionChars}x more but provide significantly better quality.";
    }
}

#endregion
