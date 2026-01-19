using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Infrastructure.Configuration;
using Google.Cloud.Speech.V2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Protobuf;
using Grpc.Core; // ✅ Fix RpcException
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class GoogleStreamingSTTService : IStreamingSTTService
{
    private readonly ILogger<GoogleStreamingSTTService> _logger;
    private readonly GoogleOptions _googleOptions;
    private readonly SpeechClient? _speechClient;

    /// <summary>
    /// Google Cloud Speech-to-Text V2 Supported Languages
    /// Updated from official documentation: https://cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages
    /// Includes all chirp_2 and chirp_3 model supported languages (latest as of Jan 2025)
    /// </summary>
    public static readonly Dictionary<string, string> GoogleSTTLanguages = new()
    {
        // English variants
        {"en-AU", "English (Australia)"},
        {"en-GB", "English (United Kingdom)"},
        {"en-IN", "English (India)"},
        {"en-PH", "English (Philippines)"},
        {"en-US", "English (United States)"},
        
        // Spanish variants
        {"es-419", "Spanish (Latin American)"},
        {"es-ES", "Spanish (Spain)"},
        {"es-MX", "Spanish (Mexico)"},
        {"es-US", "Spanish (United States)"},
        
        // French variants
        {"fr-CA", "French (Canada)"},
        {"fr-FR", "French (France)"},
        
        // Portuguese variants
        {"pt-BR", "Portuguese (Brazil)"},
        {"pt-PT", "Portuguese (Portugal)"},
        
        // Chinese variants
        {"cmn-Hans-CN", "Chinese (Simplified, China)"},
        {"cmn-Hant-TW", "Chinese, Mandarin (Traditional, Taiwan)"},
        {"yue-Hant-HK", "Chinese, Cantonese (Traditional Hong Kong)"},
        
        // Arabic variants
        {"ar-AE", "Arabic (United Arab Emirates)"},
        {"ar-BH", "Arabic (Bahrain)"},
        {"ar-DZ", "Arabic (Algeria)"},
        {"ar-EG", "Arabic (Egypt)"},
        {"ar-IL", "Arabic (Israel)"},
        {"ar-IQ", "Arabic (Iraq)"},
        {"ar-JO", "Arabic (Jordan)"},
        {"ar-KW", "Arabic (Kuwait)"},
        {"ar-LB", "Arabic (Lebanon)"},
        {"ar-MA", "Arabic (Morocco)"},
        {"ar-MR", "Arabic (Mauritania)"},
        {"ar-OM", "Arabic (Oman)"},
        {"ar-PS", "Arabic (State of Palestine)"},
        {"ar-QA", "Arabic (Qatar)"},
        {"ar-SA", "Arabic (Saudi Arabia)"},
        {"ar-SY", "Arabic (Syria)"},
        {"ar-TN", "Arabic (Tunisia)"},
        {"ar-XA", "Arabic (Pseudo-Accents)"},
        {"ar-YE", "Arabic (Yemen)"},
        
        // Major European languages
        {"de-DE", "German (Germany)"},
        {"it-IT", "Italian (Italy)"},
        {"ru-RU", "Russian (Russia)"},
        {"nl-NL", "Dutch (Netherlands)"},
        {"tr-TR", "Turkish (Turkey)"},
        {"pl-PL", "Polish (Poland)"},
        {"cs-CZ", "Czech (Czech Republic)"},
        {"sk-SK", "Slovak (Slovakia)"},
        {"hu-HU", "Hungarian (Hungary)"},
        {"ro-RO", "Romanian (Romania)"},
        {"bg-BG", "Bulgarian (Bulgaria)"},
        {"hr-HR", "Croatian (Croatia)"},
        {"sr-RS", "Serbian (Serbia)"},
        {"sl-SI", "Slovenian (Slovenia)"},
        {"mk-MK", "Macedonian (North Macedonia)"},
        {"el-GR", "Greek (Greece)"},
        {"et-EE", "Estonian (Estonia)"},
        {"lv-LV", "Latvian (Latvia)"},
        {"lt-LT", "Lithuanian (Lithuania)"},
        {"fi-FI", "Finnish (Finland)"},
        {"sv-SE", "Swedish (Sweden)"},
        {"da-DK", "Danish (Denmark)"},
        {"no-NO", "Norwegian Bokmål (Norway)"},
        {"is-IS", "Icelandic (Iceland)"},
        
        // Asian languages
        {"ja-JP", "Japanese (Japan)"},
        {"ko-KR", "Korean (South Korea)"},
        {"hi-IN", "Hindi (India)"},
        {"vi-VN", "Vietnamese (Vietnam)"},
        {"th-TH", "Thai (Thailand)"},
        {"id-ID", "Indonesian (Indonesia)"},
        {"ms-MY", "Malay (Malaysia)"},
        {"fil-PH", "Filipino (Philippines)"},
        {"jv-ID", "Javanese (Indonesia)"},
        
        // Indian languages
        {"bn-BD", "Bengali (Bangladesh)"},
        {"bn-IN", "Bengali (India)"},
        {"gu-IN", "Gujarati (India)"},
        {"kn-IN", "Kannada (India)"},
        {"ml-IN", "Malayalam (India)"},
        {"mr-IN", "Marathi (India)"},
        {"ne-NP", "Nepali (Nepal)"},
        {"or-IN", "Oriya (India)"},
        {"pa-Guru-IN", "Punjabi (Gurmukhi India)"},
        {"ta-IN", "Tamil (India)"},
        {"te-IN", "Telugu (India)"},
        {"ur-PK", "Urdu (Pakistan)"},
        {"as-IN", "Assamese (India)"},
        
        // Other languages
        {"af-ZA", "Afrikaans (South Africa)"},
        {"am-ET", "Amharic (Ethiopia)"},
        {"az-AZ", "Azerbaijani (Azerbaijan)"},
        {"be-BY", "Belarusian (Belarus)"},
        {"bs-BA", "Bosnian (Bosnia and Herzegovina)"},
        {"ca-ES", "Catalan (Spain)"},
        {"eu-ES", "Basque (Spain)"},
        {"fa-IR", "Persian (Iran)"},
        {"ga-IE", "Irish (Ireland)"},
        {"gl-ES", "Galician (Spain)"},
        {"ha-NG", "Hausa (Nigeria)"},
        {"iw-IL", "Hebrew (Israel)"},
        {"hy-AM", "Armenian (Armenia)"},
        {"ig-NG", "Igbo (Nigeria)"},
        {"ka-GE", "Georgian (Georgia)"},
        {"kk-KZ", "Kazakh (Kazakhstan)"},
        {"km-KH", "Khmer (Cambodia)"},
        {"ky-KG", "Kyrgyz (Cyrillic)"},
        {"lo-LA", "Lao (Laos)"},
        {"mn-MN", "Mongolian (Mongolia)"},
        {"my-MM", "Burmese (Myanmar)"},
        {"nso-ZA", "Sepedi (South Africa)"},
        {"so-SO", "Somali"},
        {"sq-AL", "Albanian (Albania)"},
        {"sw", "Swahili"},
        {"sw-KE", "Swahili (Kenya)"},
        {"tg-TJ", "Tajik (Tajikistan)"},
        {"uk-UA", "Ukrainian (Ukraine)"},
        {"uz-UZ", "Uzbek (Uzbekistan)"},
        {"xh-ZA", "Xhosa (South Africa)"},
        {"yo-NG", "Yoruba (Nigeria)"},
        {"zu-ZA", "Zulu (South Africa)"},
        {"cy-GB", "Welsh (United Kingdom)"}
    };

    public GoogleStreamingSTTService(
        ILogger<GoogleStreamingSTTService> logger,
        IOptions<ServiceOptions> serviceOptions)
    {
        _logger = logger;
        _googleOptions = serviceOptions.Value.Google;
        
        try
        {
            string credentialsPath = _googleOptions.CredentialsPath;
            if (System.IO.File.Exists(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            }

            // CRITICAL FIX: Regional Endpoint must match the location
            string location = !string.IsNullOrEmpty(_googleOptions.Location) ? _googleOptions.Location : "europe-west4";
            
            // If ApiEndpoint is not manually provided, we build the regional one automatically
            string finalEndpoint = !string.IsNullOrEmpty(_googleOptions.ApiEndpoint) 
                ? _googleOptions.ApiEndpoint 
                : $"{location}-speech.googleapis.com";

            _logger.LogInformation("🌐 Initializing Google STT for Location: {Location} at Endpoint: {Endpoint}", location, finalEndpoint);

            _speechClient = new SpeechClientBuilder 
            { 
                Endpoint = finalEndpoint 
            }.Build();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Google Cloud Speech client.");
        }
    }

    private string MapToGoogleLanguageCode(string language) =>
        GoogleSTTLanguages.ContainsKey(language) ? language : "en-US";

    /// <summary>
    /// Map Google STT response language codes to our standard BCP-47 format
    /// Google sometimes returns simplified codes like 'ur' instead of 'ur-PK'
    /// </summary>
    private string MapFromGoogleResponseLanguage(string? googleLanguageCode)
    {
        if (string.IsNullOrEmpty(googleLanguageCode))
            return "en-US";

        // Direct match first
        if (GoogleSTTLanguages.ContainsKey(googleLanguageCode))
            return googleLanguageCode;

        // Map simplified codes to full BCP-47 codes
        return googleLanguageCode switch
        {
            "ur" => "ur-PK",
            "ar" => "ar-SA", // Default Arabic to Saudi Arabia
            "zh" => "cmn-Hans-CN", // Default Chinese to Simplified Mandarin
            "zh-cn" => "cmn-Hans-CN",
            "zh-tw" => "cmn-Hant-TW", 
            "zh-hk" => "yue-Hant-HK",
            "en" => "en-US",
            "es" => "es-ES",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "it" => "it-IT",
            "pt" => "pt-PT",
            "ru" => "ru-RU",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "hi" => "hi-IN",
            "bn" => "bn-IN",
            "ta" => "ta-IN",
            "te" => "te-IN",
            "mr" => "mr-IN",
            "gu" => "gu-IN",
            "kn" => "kn-IN",
            "ml" => "ml-IN",
            "pa" => "pa-Guru-IN",
            "ne" => "ne-NP",
            "th" => "th-TH",
            "vi" => "vi-VN",
            "id" => "id-ID",
            "ms" => "ms-MY",
            "fil" => "fil-PH",
            "sw" => "sw-KE",
            "af" => "af-ZA",
            "tr" => "tr-TR",
            "nl" => "nl-NL",
            "sv" => "sv-SE",
            "da" => "da-DK",
            "no" => "no-NO",
            "fi" => "fi-FI",
            "pl" => "pl-PL",
            "cs" => "cs-CZ",
            "sk" => "sk-SK",
            "hu" => "hu-HU",
            "ro" => "ro-RO",
            "bg" => "bg-BG",
            "hr" => "hr-HR",
            "sr" => "sr-RS",
            "sl" => "sl-SI",
            "el" => "el-GR",
            "he" or "iw" => "iw-IL",
            "uk" => "uk-UA",
            "be" => "be-BY",
            "et" => "et-EE",
            "lv" => "lv-LV",
            "lt" => "lt-LT",
            "mk" => "mk-MK",
            "is" => "is-IS",
            "mt" => "mt-MT",
            "ga" => "ga-IE",
            "cy" => "cy-GB",
            "eu" => "eu-ES",
            "ca" => "ca-ES",
            "gl" => "gl-ES",
            "fa" => "fa-IR",
            "ka" => "ka-GE",
            "hy" => "hy-AM",
            "az" => "az-AZ",
            "kk" => "kk-KZ",
            "ky" => "ky-KG",
            "uz" => "uz-UZ",
            "tg" => "tg-TJ",
            "mn" => "mn-MN",
            "my" => "my-MM",
            "km" => "km-KH",
            "lo" => "lo-LA",
            "am" => "am-ET",
            "so" => "so-SO",
            "ha" => "ha-NG",
            "yo" => "yo-NG",
            "ig" => "ig-NG",
            "zu" => "zu-ZA",
            "xh" => "xh-ZA",
            "nso" => "nso-ZA",
            "sq" => "sq-AL",
            "bs" => "bs-BA",
            _ => googleLanguageCode // Return as-is if no mapping found
        };
    }

    public async IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"TIMESTAMP_STT_METHOD_START: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT ProcessAutoLanguageDetectionAsync started");
        
        if (_speechClient == null)
        {
            _logger.LogError("SpeechClient is null.");
            yield break;
        }

        var projectId = GetProjectId().Trim();
        var location = (!string.IsNullOrEmpty(_googleOptions.Location) ? _googleOptions.Location : "eu").Trim();
        
        var recognizerId = (!string.IsNullOrEmpty(_googleOptions.RecognizerId) ? _googleOptions.RecognizerId : "my-realtime-recognizer").Trim();
        string recognizerPath = $"projects/{projectId}/locations/{location}/recognizers/{recognizerId}";

        Console.WriteLine($"TIMESTAMP_STT_BEFORE_STREAM_ATTEMPT: {DateTime.UtcNow:HH:mm:ss.fff} - About to call ProcessGoogleStreamAttemptAsync");
        
        // 🔄 SIMPLE KEEP-ALIVE STRATEGY - Stream until utterance completion
        await foreach (var result in ProcessGoogleStreamAttemptAsync(audioStream, candidateLanguages, projectId, location, recognizerPath, cancellationToken))
        {
            Console.WriteLine($"TIMESTAMP_STT_RESULT_YIELDED: {DateTime.UtcNow:HH:mm:ss.fff} - STT result yielded: '{result.Text}' IsFinal: {result.IsFinal}");
            yield return result;
        }
        
        Console.WriteLine($"TIMESTAMP_STT_METHOD_END: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT ProcessAutoLanguageDetectionAsync completed");
    }

    private async IAsyncEnumerable<TranscriptionResult> ProcessGoogleStreamAttemptAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        string projectId,
        string location,
        string recognizerPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Console.WriteLine($"TIMESTAMP_STT_STREAM_ATTEMPT_START: {DateTime.UtcNow:HH:mm:ss.fff} - ProcessGoogleStreamAttemptAsync started");
        
        var config = new RecognitionConfig
        {
            LanguageCodes = { new[] { "en-US" } },
            ExplicitDecodingConfig = new ExplicitDecodingConfig
            {
                Encoding = ExplicitDecodingConfig.Types.AudioEncoding.WebmOpus,
                SampleRateHertz = 48000,
                AudioChannelCount = 1
            }
        };

        var streamingCall = _speechClient!.StreamingRecognize();

        Console.WriteLine($"TIMESTAMP_STT_STREAM_CREATED: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT streaming call created");

        // HANDSHAKE
        await streamingCall.WriteAsync(new StreamingRecognizeRequest
        {
            Recognizer = recognizerPath,
            StreamingConfig = new StreamingRecognitionConfig
            {
                Config = config,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true,
                    EnableVoiceActivityEvents = true 
                }
            }
        });

        Console.WriteLine($"TIMESTAMP_STT_HANDSHAKE_COMPLETE: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT handshake completed");
        _logger.LogInformation("🎤 Google STT Stream Started (Region: {Region}) - Keep-alive until utterance completion", location);

        // 🔄 AUDIO PUSHER TASK - Keeps stream alive by sending audio chunks
        var audioPushTask = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_START: {DateTime.UtcNow:HH:mm:ss.fff} - Audio push task started");
                bool firstChunk = true;
                
                await foreach (var chunk in audioStream.ReadAllAsync(cancellationToken))
                {
                    if (firstChunk)
                    {
                        Console.WriteLine($"TIMESTAMP_STT_FIRST_CHUNK: {DateTime.UtcNow:HH:mm:ss.fff} - First audio chunk received, size: {chunk.Length}");
                        firstChunk = false;
                    }
                    
                    if (chunk.Length > 0) // Only send non-empty audio chunks
                    {
                        await streamingCall.WriteAsync(new StreamingRecognizeRequest { Audio = ByteString.CopyFrom(chunk) });
                    }
                }
                
                // ⚡ IMMEDIATE STREAM COMPLETION: Close Google STT stream as soon as audio ends
                Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_END: {DateTime.UtcNow:HH:mm:ss.fff} - Audio stream ended - immediately completing Google STT stream");
                await streamingCall.WriteCompleteAsync();
                Console.WriteLine($"TIMESTAMP_STT_STREAM_COMPLETED: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT stream completed successfully");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_CANCELLED: {DateTime.UtcNow:HH:mm:ss.fff} - Audio stream cancelled - completing Google STT stream");
                // ⚡ GRACEFUL CANCELLATION: Still close stream properly to avoid timeout
                try 
                { 
                    await streamingCall.WriteCompleteAsync(); 
                    Console.WriteLine($"TIMESTAMP_STT_STREAM_CLOSED_ON_CANCEL: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT stream closed gracefully on cancellation");
                } 
                catch (Exception ex)
                {
                    Console.WriteLine($"TIMESTAMP_STT_STREAM_CLOSE_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - Error closing stream: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - Audio push error: {ex.Message}");
                _logger.LogWarning(ex, "⚠️ Audio push error - Google STT stream will naturally close");
                // Still try to close stream
                try { await streamingCall.WriteCompleteAsync(); } catch { }
            }
            finally
            {
                Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_CLEANUP: {DateTime.UtcNow:HH:mm:ss.fff} - Audio push task cleanup completed");
            }
        }, cancellationToken);

        // 📤 RESPONSE YIELDING - Stream stays alive until cancellation or completion
        var responseEnumerator = streamingCall.GetResponseStream().WithCancellation(cancellationToken);
        
        await foreach (var response in responseEnumerator)
        {
            if (response.Results?.Count > 0)
            {
                foreach (var result in response.Results)
                {
                    if (result.Alternatives?.Count > 0)
                    {
                        var alt = result.Alternatives[0];
                        var speakerLabel = alt.Words.FirstOrDefault()?.SpeakerLabel ?? "0";
                        
                        yield return new TranscriptionResult
                        {
                            Text = alt.Transcript,
                            IsFinal = result.IsFinal,
                            Confidence = alt.Confidence,
                            Language = MapFromGoogleResponseLanguage(result.LanguageCode)
                        };
                    }
                }
            }
        }
        
        Console.WriteLine($"TIMESTAMP_STT_RESPONSE_STREAM_ENDED: {DateTime.UtcNow:HH:mm:ss.fff} - Google STT response stream ended");
        _logger.LogInformation("✅ Google STT stream completed (utterance finished)");
        
        // Ensure audio push task completes
        try
        {
            await audioPushTask;
            Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_AWAITED: {DateTime.UtcNow:HH:mm:ss.fff} - Audio push task completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TIMESTAMP_STT_AUDIO_PUSH_AWAIT_ERROR: {DateTime.UtcNow:HH:mm:ss.fff} - Error waiting for audio push: {ex.Message}");
            _logger.LogWarning(ex, "⚠️ Error waiting for audio push task completion");
        }
    }

    private string GetProjectId()
    {
        return (!string.IsNullOrEmpty(_googleOptions.ProjectId) 
            ? _googleOptions.ProjectId 
            : Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? "your-project-id").Trim();
    }
}