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

    public async IAsyncEnumerable<TranscriptionResult> TranscribeStreamAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🟢 DEBUG: Google STT Starting for language: {language}");
        _logger.LogInformation("🎤 Starting Google Streaming STT for {Language}", language);

        if (_speechClient == null)
        {
            Console.WriteLine($"❌ Google STT: SpeechClient is null - returning mock result");
            _logger.LogWarning("Google Speech client not available, using mock results");
            yield return new TranscriptionResult
            {
                Text = "[Google Mock - No Credentials] Audio processed",
                Language = language,
                Confidence = 0.5,
                IsFinal = true
            };
            yield break;
        }

        Console.WriteLine($"✅ Google STT: SpeechClient available, starting processing for {language}");
        await foreach (var result in ProcessGoogleStreamingAsync(audioStream, language, cancellationToken))
        {
            Console.WriteLine($"🟢 Google STT: Yielding result for {language}: \"{result.Text}\" (IsFinal: {result.IsFinal})");
            yield return result;
        }
    }

    /// <summary>
    /// Process streaming WebM/Opus audio directly without conversion
    /// This method streams WebM chunks directly to Google STT
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> TranscribeWebMStreamAsync(
        ChannelReader<byte[]> webmStream,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🎵 DEBUG: Google STT WebM Starting for language: {language}");
        _logger.LogInformation("🎤 Starting Google Streaming WebM STT for {Language}", language);

        if (_speechClient == null)
        {
            Console.WriteLine($"❌ Google STT WebM: SpeechClient is null - returning mock result");
            _logger.LogWarning("Google Speech client not available, using mock results");
            yield return new TranscriptionResult
            {
                Text = "[Google WebM Mock - No Credentials] Audio processed",
                Language = language,
                Confidence = 0.5,
                IsFinal = true
            };
            yield break;
        }

        Console.WriteLine($"✅ Google STT WebM: SpeechClient available, starting WebM processing for {language}");
        await foreach (var result in ProcessGoogleWebMStreamingAsync(webmStream, new string[] { language }, cancellationToken))
        {
            Console.WriteLine($"🎵 Google STT WebM: Yielding result for {language}: \"{result.Text}\" (IsFinal: {result.IsFinal})");
            yield return result;
        }
    }

    private async IAsyncEnumerable<TranscriptionResult> ProcessGoogleStreamingAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resultQueue = new Queue<TranscriptionResult>();
        var hasError = false;
        
        try
        {
            Console.WriteLine($"🟢 DEBUG: Google STT ProcessGoogleStreamingAsync starting for {language}");
            
            var mappedLanguage = MapToGoogleLanguageCode(language);
            Console.WriteLine($"🟢 DEBUG: Google STT Config - Input Language: {language}, Mapped: {mappedLanguage}");
            
            // Official Google Cloud Speech V2 Linear16 configuration pattern
            var config = new RecognitionConfig
            {
                LanguageCodes = { mappedLanguage },
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 16000,
                    AudioChannelCount = 1
                },
                Model = "long",
                Features = new RecognitionFeatures
                {
                    EnableAutomaticPunctuation = true
                }
            };

            Console.WriteLine($"🟢 DEBUG: Google STT Config - Encoding: {config.ExplicitDecodingConfig.Encoding}");
            Console.WriteLine($"🟢 DEBUG: Google STT Config - Sample Rate: {config.ExplicitDecodingConfig.SampleRateHertz}Hz");
            Console.WriteLine($"🟢 DEBUG: Google STT Config - Language Codes: [{string.Join(", ", config.LanguageCodes)}]");

            var streamingConfig = new StreamingRecognitionConfig
            {
                Config = config,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true
                    // Note: VoiceActivityTimeout removed to match Python samples exactly
                    // Can be re-added if needed for specific use cases
                }
            };

            Console.WriteLine($"🟢 DEBUG: Google STT - Creating streaming recognizer...");
            var stream = _speechClient!.StreamingRecognize();

            var projectId = GetProjectId();
            Console.WriteLine($"🟢 DEBUG: Google STT Config - Project ID: {projectId}");
            Console.WriteLine($"🟢 DEBUG: Google STT - Sending initial recognition request...");
            
            await stream.WriteAsync(new StreamingRecognizeRequest
            {
                Recognizer = $"projects/{projectId}/locations/global/recognizers/_",
                StreamingConfig = streamingConfig
            });

            Console.WriteLine($"🟢 DEBUG: Google STT - Starting audio send task...");
            var audioSendTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"🟢 DEBUG: Google STT starting audio send task for {language}");
                    var chunkCount = 0;
                    var totalBytes = 0;
                    
                    await foreach (var audioChunk in audioStream.ReadAllAsync(cancellationToken))
                    {
                        chunkCount++;
                        totalBytes += audioChunk?.Length ?? 0;
                        
                        if (chunkCount == 1)
                        {
                            Console.WriteLine($"🟢 Google STT: Received first audio chunk ({audioChunk?.Length} bytes) for {language}");
                        }
                        
                        if (audioChunk?.Length > 0)
                        {
                            if (chunkCount % 10 == 0) // Log every 10th chunk
                            {
                                Console.WriteLine($"🟢 Google STT: Sent chunk #{chunkCount} ({audioChunk.Length} bytes) for {language}");
                            }
                            
                            await stream.WriteAsync(new StreamingRecognizeRequest
                            {
                                Audio = ByteString.CopyFrom(audioChunk)
                            });
                        }
                    }
                    
                    Console.WriteLine($"🟢 Google STT Audio Send Completed for {language}: {chunkCount} chunks, {totalBytes} total bytes");
                }
                finally
                {
                    await stream.WriteCompleteAsync();
                }
            }, cancellationToken);

            // ✅ Fixed: Use proper await foreach instead of MoveNext
            await foreach (var response in stream.GetResponseStream().WithCancellation(cancellationToken))
            {
                Console.WriteLine($"🟢 Google STT: Received response for {language} with {response.Results.Count} results");
                
                foreach (var result in response.Results)
                {
                    Console.WriteLine($"🟢 Google STT: Processing result - IsFinal: {result.IsFinal}, Alternatives: {result.Alternatives.Count}");
                    
                    foreach (var alternative in result.Alternatives)
                    {
                        Console.WriteLine($"🟢 Google STT RESULT for {language}: \"{alternative.Transcript}\" (Confidence: {alternative.Confidence:P1}, IsFinal: {result.IsFinal})");
                        _logger.LogInformation("🟢 Google STT Result for {Language}: \"{Text}\" (Confidence: {Confidence:P1}, IsFinal: {IsFinal})", 
                            language, alternative.Transcript, alternative.Confidence, result.IsFinal);
                        
                        resultQueue.Enqueue(new TranscriptionResult
                        {
                            Text = alternative.Transcript,
                            Language = language,
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
            _logger.LogInformation("🛑 Google STT stream cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Google STT stream error");
            hasError = true;
        }

        // ✅ Yield results after try/catch block
        while (resultQueue.Count > 0)
        {
            yield return resultQueue.Dequeue();
        }

        // ✅ Yield error result if needed (outside catch)
        if (hasError)
        {
            yield return new TranscriptionResult
            {
                Text = "[Google Error Fallback] Processing failed",
                Language = language,
                Confidence = 0.3,
                IsFinal = true
            };
        }
    }

    private async IAsyncEnumerable<TranscriptionResult> ProcessGoogleWebMStreamingAsync(
        ChannelReader<byte[]> webmStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resultQueue = new Queue<TranscriptionResult>();
        var hasError = false;
        
        try
        {
            var primaryLanguage = candidateLanguages.FirstOrDefault() ?? "en-US";
            Console.WriteLine($"🎵 DEBUG: Google STT WebM ProcessGoogleWebMStreamingAsync starting with {candidateLanguages.Length} candidate languages, primary: {primaryLanguage}");
            
            var mappedPrimaryLanguage = MapToGoogleLanguageCode(primaryLanguage);
            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Primary Language: {primaryLanguage}, Mapped: {mappedPrimaryLanguage}");
            
            // Official Google Cloud Speech V2 WebM configuration pattern
            var config = new RecognitionConfig
            {
                LanguageCodes = { mappedPrimaryLanguage },
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.WebmOpus,
                    SampleRateHertz = 48000, // WebM/Opus typically uses 48kHz
                    AudioChannelCount = 1
                },
                Model = "long",
                Features = new RecognitionFeatures
                {
                    EnableAutomaticPunctuation = true
                }
            };

            // Add candidate languages for detection (limit to avoid API errors)
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

            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Encoding: {config.ExplicitDecodingConfig.Encoding}");
            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Sample Rate: {config.ExplicitDecodingConfig.SampleRateHertz}Hz");
            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Language Codes: [{string.Join(", ", config.LanguageCodes)}]");

            var streamingConfig = new StreamingRecognitionConfig
            {
                Config = config,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true
                }
            };

            Console.WriteLine($"🎵 DEBUG: Google STT WebM - Creating streaming recognizer...");
            var stream = _speechClient!.StreamingRecognize();

            Console.WriteLine($"🎵 DEBUG: Google STT WebM - Sending initial config request...");
            
            var projectId = GetProjectId();
            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Project ID: {projectId}");
            
            // Send initial configuration
            await stream.WriteAsync(new StreamingRecognizeRequest
            {
                Recognizer = $"projects/{projectId}/locations/global/recognizers/_", 
                StreamingConfig = streamingConfig
            });

            Console.WriteLine($"🎵 DEBUG: Google STT WebM - Starting audio streaming...");

            // Start WebM audio sending task
            var audioSendTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"🎵 DEBUG: Google STT WebM starting audio send task for candidates: [{string.Join(", ", candidateLanguages)}]");
                    var chunkCount = 0;
                    var totalBytes = 0;
                    
                    await foreach (var webmChunk in webmStream.ReadAllAsync(cancellationToken))
                    {
                        chunkCount++;
                        totalBytes += webmChunk?.Length ?? 0;
                        
                        if (chunkCount == 1)
                        {
                            Console.WriteLine($"🎵 Google STT WebM: Received first WebM chunk ({webmChunk?.Length} bytes) for candidates: [{string.Join(", ", candidateLanguages)}]");
                        }
                        
                        if (webmChunk?.Length > 0)
                        {
                            if (chunkCount % 10 == 0) // Log every 10th chunk
                            {
                                Console.WriteLine($"🎵 Google STT WebM: Sent chunk #{chunkCount} ({webmChunk.Length} bytes) for candidates: [{string.Join(", ", candidateLanguages)}]");
                            }
                            
                            await stream.WriteAsync(new StreamingRecognizeRequest
                            {
                                Audio = ByteString.CopyFrom(webmChunk)
                            });
                        }
                    }
                    
                    Console.WriteLine($"🎵 Google STT WebM Audio Send Completed for candidates [{string.Join(", ", candidateLanguages)}]: {chunkCount} chunks, {totalBytes} total bytes");
                }
                finally
                {
                    await stream.WriteCompleteAsync();
                }
            }, cancellationToken);

            // Process responses
            await foreach (var response in stream.GetResponseStream().WithCancellation(cancellationToken))
            {
                Console.WriteLine($"🎵 Google STT WebM: Received response for candidates [{string.Join(", ", candidateLanguages)}] with {response.Results.Count} results");
                
                foreach (var result in response.Results)
                {
                    Console.WriteLine($"🎵 Google STT WebM: Processing result - IsFinal: {result.IsFinal}, Alternatives: {result.Alternatives.Count}");
                    
                    foreach (var alternative in result.Alternatives)
                    {
                        // Detect language from the result
                        var detectedLanguage = !string.IsNullOrEmpty(result.LanguageCode) 
                            ? result.LanguageCode 
                            : candidateLanguages.FirstOrDefault() ?? "en-US";
                        
                        Console.WriteLine($"🎵 Google STT WebM RESULT for detected {detectedLanguage}: \"{alternative.Transcript}\" (Confidence: {alternative.Confidence:P1}, IsFinal: {result.IsFinal})");
                        _logger.LogInformation("🎵 Google STT WebM Result for {Language}: \"{Text}\" (Confidence: {Confidence:P1}, IsFinal: {IsFinal})", 
                            detectedLanguage, alternative.Transcript, alternative.Confidence, result.IsFinal);
                        
                        resultQueue.Enqueue(new TranscriptionResult
                        {
                            Text = alternative.Transcript,
                            Language = detectedLanguage,  // Use detected language
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
            _logger.LogInformation("🛑 Google STT WebM stream cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Google STT WebM stream error");
            hasError = true;
        }

        // Yield results after try/catch block
        while (resultQueue.Count > 0)
        {
            yield return resultQueue.Dequeue();
        }

        // Yield error result if needed (outside catch)
        if (hasError)
        {
            yield return new TranscriptionResult
            {
                Text = "[Google WebM Error Fallback] Processing failed",
                Language = candidateLanguages.FirstOrDefault() ?? "en-US",
                Confidence = 0.3,
                IsFinal = true
            };
        }
    }

    public async Task<TranscriptionResult> TranscribeAudioAsync(
        byte[] audioData, 
        string language, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎤 Google single audio transcription for {Language}", language);

        if (_speechClient == null)
        {
            return new TranscriptionResult
            {
                Text = "[Google Mock - No Credentials] Single audio processed",
                Language = language,
                Confidence = 0.5,
                IsFinal = true
            };
        }

        try
        {
            var config = new RecognitionConfig
            {
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 16000,
                    AudioChannelCount = 1
                },
                LanguageCodes = { MapToGoogleLanguageCode(language) }
            };

            var request = new RecognizeRequest
            {
                Recognizer = $"projects/{GetProjectId()}/locations/global/recognizers/_",
                Config = config,
                Content = ByteString.CopyFrom(audioData)
            };

            var response = await _speechClient.RecognizeAsync(request, cancellationToken);

            var bestResult = response.Results
                .SelectMany(r => r.Alternatives)
                .OrderByDescending(a => a.Confidence)
                .FirstOrDefault();

            return new TranscriptionResult
            {
                Text = bestResult?.Transcript ?? "",
                Language = language,
                Confidence = bestResult?.Confidence ?? 0.0,
                IsFinal = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Google STT transcription error");
            return new TranscriptionResult
            {
                Text = "",
                Language = language,
                Confidence = 0.0,
                IsFinal = true
            };
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
            Console.WriteLine($"🌍 Google STT Auto-Detection: Yielding result: \"{result.Text}\" (Language: {result.Language}, IsFinal: {result.IsFinal})");
            yield return result;
        }
    }

    /// <summary>
    /// [Obsolete] Use ProcessAutoLanguageDetectionAsync instead
    /// Transcribe audio stream with Google's automatic language detection
    /// </summary>
    [Obsolete("Use ProcessAutoLanguageDetectionAsync instead. This method will be removed in a future version.", false)]
    public async IAsyncEnumerable<TranscriptionResult> TranscribeStreamWithAutoDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in ProcessAutoLanguageDetectionAsync(audioStream, candidateLanguages, cancellationToken))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<TranscriptionResult> ProcessGoogleAutoDetectionAsync(
        ChannelReader<byte[]> audioStream,
        string[] candidateLanguages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resultQueue = new Queue<TranscriptionResult>();
        var hasError = false;
        
        try
        {
            Console.WriteLine($"🌍 DEBUG: Google STT Auto-Detection starting with {candidateLanguages.Length} candidate languages");
            
            var primaryLanguage = candidateLanguages.FirstOrDefault() ?? "en-US";
            var mappedPrimaryLanguage = MapToGoogleLanguageCode(primaryLanguage);
            Console.WriteLine($"🌍 DEBUG: Google STT Auto-Detection Config - Primary Language: {primaryLanguage}, Mapped: {mappedPrimaryLanguage}");
            
            // Official Google Cloud Speech V2 WebM configuration pattern (copy from working WebM method)
            var config = new RecognitionConfig
            {
                LanguageCodes = { mappedPrimaryLanguage },
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.WebmOpus,
                    SampleRateHertz = 48000, // WebM/Opus typically uses 48kHz
                    AudioChannelCount = 1
                },
                Model = "long",
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

            Console.WriteLine($"🌍 Google Auto-Detection Config - Languages: [{string.Join(", ", config.LanguageCodes)}]");
            Console.WriteLine($"🌍 Google Auto-Detection Config - Encoding: {config.ExplicitDecodingConfig.Encoding}");
            Console.WriteLine($"🌍 Google Auto-Detection Config - Sample Rate: {config.ExplicitDecodingConfig.SampleRateHertz}Hz");

            var streamingConfig = new StreamingRecognitionConfig
            {
                Config = config,
                StreamingFeatures = new StreamingRecognitionFeatures
                {
                    InterimResults = true
                }
            };

            Console.WriteLine($"🌍 Google Auto-Detection: Creating streaming recognizer...");
            var stream = _speechClient!.StreamingRecognize();

            Console.WriteLine($"🌍 Google Auto-Detection: Sending initial config request...");
            
            var projectId = GetProjectId();
            Console.WriteLine($"🌍 Google Auto-Detection Config - Project ID: {projectId}");
            
            // Send initial configuration (exact pattern from WebM method)
            await stream.WriteAsync(new StreamingRecognizeRequest
            {
                Recognizer = $"projects/{projectId}/locations/global/recognizers/_", 
                StreamingConfig = streamingConfig
            });

            Console.WriteLine($"🌍 Google Auto-Detection: Starting audio streaming...");

            // Start audio sending task (exact pattern from WebM method)
            var audioSendTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"🌍 DEBUG: Google Auto-Detection starting audio send task");
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
                            if (chunkCount % 10 == 0) // Log every 10th chunk
                            {
                                Console.WriteLine($"🌍 Google Auto-Detection: Sent chunk #{chunkCount} ({chunk.Length} bytes)");
                            }
                            
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
                Console.WriteLine($"🌍 Google Auto-Detection: Received response with {response.Results.Count} results");
                
                foreach (var result in response.Results)
                {
                    Console.WriteLine($"🌍 Google Auto-Detection: Processing result - IsFinal: {result.IsFinal}, Alternatives: {result.Alternatives.Count}");
                    
                    foreach (var alternative in result.Alternatives)
                    {
                        // Use Google's detected language from the result (key for auto-detection)
                        var detectedLanguage = !string.IsNullOrEmpty(result.LanguageCode) 
                            ? result.LanguageCode 
                            : primaryLanguage; // Fallback to primary language
                        
                        Console.WriteLine($"🌍 Google Auto-Detection RESULT: [{detectedLanguage}] \"{alternative.Transcript}\" (Confidence: {alternative.Confidence:P1}, IsFinal: {result.IsFinal})");
                        _logger.LogInformation("🌍 Google Auto-Detection Result: [{Language}] \"{Text}\" (Confidence: {Confidence:P1}, IsFinal: {IsFinal})", 
                            detectedLanguage, alternative.Transcript, alternative.Confidence, result.IsFinal);
                        
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