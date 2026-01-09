using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Infrastructure.Configuration;
using Google.Cloud.Speech.V2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Protobuf;

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
            // ✅ Use configuration instead of hardcoded path
            string credentialsPath = _googleOptions.CredentialsPath;
            
            if (System.IO.File.Exists(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            }
            else
            {
                Console.WriteLine($"❌ Google STT Config - Credentials file not found at: {credentialsPath}");
                _logger.LogError("❌ Google credentials file not found at: {Path}", credentialsPath);
            }

            _speechClient = SpeechClient.Create();
          }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Google STT Config - Failed to initialize: {ex.Message}");
            _logger.LogError(ex, "❌ Failed to initialize Google Cloud Speech client. Ensure credentials are configured.");
        }
    }


    private string MapToGoogleLanguageCode(string language)
    {
        return GoogleSTTLanguages.ContainsKey(language) ? language : "en-US";
    }

    /// <summary>
    /// Process audio stream with Google's automatic language detection
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> ProcessAutoLanguageDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
   
        if (_speechClient == null)
        {
            Console.WriteLine($"❌ Google STT Auto-Detection: SpeechClient is null - returning mock result");
            _logger.LogWarning("Google Speech client not available, using mock results");
            yield return new TranscriptionResult
            {
                Text = "[Google Auto-Detection Mock - No Credentials] Audio processed",
                Language = candidateLanguages.FirstOrDefault() ?? "en-US",
                Confidence = 0.5,
                IsFinal = true
            };
            yield break;
        }

        await foreach (var result in ProcessGoogleAutoDetectionAsync(audioStream, candidateLanguages, cancellationToken))
        {
           yield return result;
        }
    }

    /// <summary>
    private async IAsyncEnumerable<TranscriptionResult> ProcessGoogleAutoDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resultQueue = new Queue<TranscriptionResult>();
        var hasError = false;
        
        try
        {
            var primaryLanguage = candidateLanguages.FirstOrDefault() ?? "en-US";
            var mappedPrimaryLanguage = MapToGoogleLanguageCode(primaryLanguage);
            
            // Simplified configuration for auto-detection - remove conflicting WebM settings
            var config = new RecognitionConfig
            {
                LanguageCodes = { mappedPrimaryLanguage },
                // Remove ExplicitDecodingConfig to let Google auto-detect audio format
                Model = "latest_long",  // Use latest_long instead of "long"
                Features = new RecognitionFeatures
                {
                    EnableAutomaticPunctuation = true
                }
            };

            // Add candidate languages for auto-detection (limit to avoid API errors)
            var maxLanguages = 3; // Following Google's recommendation
            var limitedCandidates = candidateLanguages.Where(l => l != primaryLanguage).Take(maxLanguages - 1);
            foreach (var language in limitedCandidates)
            {
                var mappedCandidate = MapToGoogleLanguageCode(language);
                if (!config.LanguageCodes.Contains(mappedCandidate))
                {
                    config.LanguageCodes.Add(mappedCandidate);
                }
            }


            var streamingConfig = new StreamingRecognitionConfig
            {
                Config = config,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true,
                    EnableVoiceActivityEvents = false  // Disable voice activity events that might cause issues
                }
            };
            var stream = _speechClient!.StreamingRecognize();
           
            var projectId = GetProjectId();
            
            // Send initial configuration with correct recognizer path for Speech V2
            await stream.WriteAsync(new StreamingRecognizeRequest
            {
                Recognizer = $"projects/{projectId}/locations/global/recognizers/_", // Use _ for default recognizer
                StreamingConfig = streamingConfig
            });

            // Start audio sending task (exact pattern from WebM method)
            var audioSendTask = Task.Run(async () =>
            {
                try
                {
                    var chunkCount = 0;
                    var totalBytes = 0;
                    
                    await foreach (var chunk in audioStream.ReadAllAsync(cancellationToken))
                    {
                        chunkCount++;
                        totalBytes += chunk?.Length ?? 0;
                        
                        if (chunkCount == 1)
                        {
                            Console.WriteLine($"🌍 Google Auto-Detection: Received first chunk ({chunk?.Length} bytes)");
                        }
                        
                        if (chunk?.Length > 0)
                        {
                            await stream.WriteAsync(new StreamingRecognizeRequest
                            {
                                Audio = ByteString.CopyFrom(chunk)
                            });
                        }
                    }
                    
                    Console.WriteLine($"🌍 Google Auto-Detection Audio Send Completed: {chunkCount} chunks, {totalBytes} total bytes");
                }
                finally
                {
                    await stream.WriteCompleteAsync();
                }
            }, cancellationToken);

            // Process responses (exact pattern from WebM method)
            await foreach (var response in stream.GetResponseStream().WithCancellation(cancellationToken))
            {               
                foreach (var result in response.Results)
                {
                   foreach (var alternative in result.Alternatives)
                    {
                        // Use Google's detected language from the result (key for auto-detection)
                        var detectedLanguage = !string.IsNullOrEmpty(result.LanguageCode) 
                            ? result.LanguageCode 
                            : primaryLanguage; // Fallback to primary language
                        
                        resultQueue.Enqueue(new TranscriptionResult
                        {
                            Text = alternative.Transcript,
                            Language = detectedLanguage, // Use Google's detected language
                            Confidence = alternative.Confidence,
                            IsFinal = result.IsFinal,
                            Timestamp = TimeSpan.FromSeconds(result.ResultEndOffset?.Seconds ?? 0)
                        });
                    }
                }
            }

            await audioSendTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🛑 Google Auto-Detection stream cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Google Auto-Detection stream error");
            hasError = true;
        }

        // Yield results after try/catch block (exact pattern from WebM method)
        while (resultQueue.Count > 0)
        {
            yield return resultQueue.Dequeue();
        }

        // Yield error result if needed (exact pattern from WebM method)
        if (hasError)
        {
            yield return new TranscriptionResult
            {
                Text = "[Google Auto-Detection Error Fallback] Processing failed",
                Language = candidateLanguages.FirstOrDefault() ?? "en-US",
                Confidence = 0.3,
                IsFinal = true
            };
        }
    }

    private string GetProjectId()
    {
        // Use configuration first, then fall back to environment variables
        var projectId = !string.IsNullOrEmpty(_googleOptions.ProjectId) 
            ? _googleOptions.ProjectId
            : Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") 
              ?? Environment.GetEnvironmentVariable("GCP_PROJECT") 
              ?? "a3itranslator"; // Your actual Google Cloud project ID
        
        Console.WriteLine($"🟢 DEBUG: Google STT - Resolved Project ID: {projectId}");
        return projectId;
    }
}