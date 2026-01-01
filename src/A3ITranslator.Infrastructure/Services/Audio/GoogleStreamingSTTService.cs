using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using Google.Cloud.Speech.V2;
using Microsoft.Extensions.Logging;
using Google.Protobuf;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class GoogleStreamingSTTService : IStreamingSTTService
{
    private readonly ILogger<GoogleStreamingSTTService> _logger;
    private readonly SpeechClient? _speechClient;

    /// <summary>
    /// Google Cloud STT Languages (Partial/Major List)
    /// </summary>
    public static readonly Dictionary<string, string> GoogleSTTLanguages = new()
    {
        {"en-US", "English (United States)"},
        {"en-GB", "English (United Kingdom)"},
        {"es-ES", "Spanish (Spain)"},
        {"es-US", "Spanish (United States)"},
        {"fr-FR", "French (France)"},
        {"de-DE", "German (Germany)"},
        {"it-IT", "Italian (Italy)"},
        {"ja-JP", "Japanese (Japan)"},
        {"ko-KR", "Korean (South Korea)"},
        {"pt-BR", "Portuguese (Brazil)"},
        {"zh-CN", "Chinese (Simplified)"},
        {"zh-TW", "Chinese (Traditional)"},
        {"ru-RU", "Russian (Russia)"},
        {"hi-IN", "Hindi (India)"},
        {"ar-SA", "Arabic (Saudi Arabia)"},
        {"nl-NL", "Dutch (Netherlands)"},
        {"tr-TR", "Turkish (Turkey)"},
        {"vi-VN", "Vietnamese (Vietnam)"},
        {"th-TH", "Thai (Thailand)"},
        {"id-ID", "Indonesian (Indonesia)"}
    };

    public GoogleStreamingSTTService(ILogger<GoogleStreamingSTTService> logger)
    {
        _logger = logger;
        
        try
        {
            // ✅ Set the credentials path provided by the user
            string credentialsPath = "/Users/farhanfarooq/Documents/GitHub/A3ITranslator/a3itranslator-9b86c705f20c.json";
            Console.WriteLine($"🟢 DEBUG: Google STT Config - Credentials Path: {credentialsPath}");
            Console.WriteLine($"🟢 DEBUG: Google STT Config - File Exists: {System.IO.File.Exists(credentialsPath)}");
            
            if (System.IO.File.Exists(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
                Console.WriteLine($"🟢 DEBUG: Google STT Config - Environment variable set successfully");
                _logger.LogInformation("🔑 Google credentials set from {Path}", credentialsPath);
            }
            else
            {
                Console.WriteLine($"❌ Google STT Config - Credentials file not found at: {credentialsPath}");
            }

            Console.WriteLine($"🟢 DEBUG: Google STT Config - Creating SpeechClient...");
            _speechClient = SpeechClient.Create();
            Console.WriteLine($"✅ Google STT Config - SpeechClient created successfully");
            _logger.LogInformation("✅ Google Cloud Speech client initialized successfully");
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
        await foreach (var result in ProcessGoogleWebMStreamingAsync(webmStream, language, cancellationToken))
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
            
            var config = new RecognitionConfig
            {
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 16000,
                    AudioChannelCount = 1
                },
                LanguageCodes = { mappedLanguage },
                Model = "long"
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
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resultQueue = new Queue<TranscriptionResult>();
        var hasError = false;
        
        try
        {
            Console.WriteLine($"🎵 DEBUG: Google STT WebM ProcessGoogleWebMStreamingAsync starting for {language}");
            
            var mappedLanguage = MapToGoogleLanguageCode(language);
            Console.WriteLine($"🎵 DEBUG: Google STT WebM Config - Input Language: {language}, Mapped: {mappedLanguage}");
            
            var config = new RecognitionConfig
            {
                ExplicitDecodingConfig = new ExplicitDecodingConfig
                {
                    Encoding = ExplicitDecodingConfig.Types.AudioEncoding.WebmOpus,
                    SampleRateHertz = 48000, // WebM/Opus typically uses 48kHz
                    AudioChannelCount = 1
                },
                LanguageCodes = { mappedLanguage },
                Model = "long"
            };

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
                    Console.WriteLine($"🎵 DEBUG: Google STT WebM starting audio send task for {language}");
                    var chunkCount = 0;
                    var totalBytes = 0;
                    
                    await foreach (var webmChunk in webmStream.ReadAllAsync(cancellationToken))
                    {
                        chunkCount++;
                        totalBytes += webmChunk?.Length ?? 0;
                        
                        if (chunkCount == 1)
                        {
                            Console.WriteLine($"🎵 Google STT WebM: Received first WebM chunk ({webmChunk?.Length} bytes) for {language}");
                        }
                        
                        if (webmChunk?.Length > 0)
                        {
                            if (chunkCount % 10 == 0) // Log every 10th chunk
                            {
                                Console.WriteLine($"🎵 Google STT WebM: Sent chunk #{chunkCount} ({webmChunk.Length} bytes) for {language}");
                            }
                            
                            await stream.WriteAsync(new StreamingRecognizeRequest
                            {
                                Audio = ByteString.CopyFrom(webmChunk)
                            });
                        }
                    }
                    
                    Console.WriteLine($"🎵 Google STT WebM Audio Send Completed for {language}: {chunkCount} chunks, {totalBytes} total bytes");
                }
                finally
                {
                    await stream.WriteCompleteAsync();
                }
            }, cancellationToken);

            // Process responses
            await foreach (var response in stream.GetResponseStream().WithCancellation(cancellationToken))
            {
                Console.WriteLine($"🎵 Google STT WebM: Received response for {language} with {response.Results.Count} results");
                
                foreach (var result in response.Results)
                {
                    Console.WriteLine($"🎵 Google STT WebM: Processing result - IsFinal: {result.IsFinal}, Alternatives: {result.Alternatives.Count}");
                    
                    foreach (var alternative in result.Alternatives)
                    {
                        Console.WriteLine($"🎵 Google STT WebM RESULT for {language}: \"{alternative.Transcript}\" (Confidence: {alternative.Confidence:P1}, IsFinal: {result.IsFinal})");
                        _logger.LogInformation("🎵 Google STT WebM Result for {Language}: \"{Text}\" (Confidence: {Confidence:P1}, IsFinal: {IsFinal})", 
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
                Language = language,
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

    private string GetProjectId()
    {
        // Try multiple ways to get project ID (matching Python sample approach)
        var projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") 
                       ?? Environment.GetEnvironmentVariable("GCP_PROJECT") 
                       ?? "a3itranslator"; // Your actual Google Cloud project ID
        
        Console.WriteLine($"🟢 DEBUG: Google STT - Resolved Project ID: {projectId}");
        return projectId;
    }
}