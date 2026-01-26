using System.Threading.Channels;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace A3ITranslator.Infrastructure.Services.Audio;

public class AzureStreamingSTTService : IStreamingSTTService
{
    private readonly ServiceOptions _options;
    private readonly ILogger<AzureStreamingSTTService> _logger;

    /// <summary>
    /// Azure STT Languages - EXACT dictionary from IMPLEMENTATION.md
    /// Tier 1 - Primary supported languages
    /// </summary>
    public static readonly Dictionary<string, string> AzureSTTLanguages = new()
    {
        // Tier 1 - Primary supported languages
        {"en-US", "English (United States)"},
        {"en-GB", "English (United Kingdom)"},
        {"en-AU", "English (Australia)"},
        {"en-CA", "English (Canada)"},
        {"en-IN", "English (India)"},
        
        // Urdu - Azure's strength
        {"ur-IN", "Urdu (India)"},
        
        // Arabic variants - Azure extensive support
        {"ar-SA", "Arabic (Saudi Arabia)"},
        {"ar-EG", "Arabic (Egypt)"},
        {"ar-AE", "Arabic (United Arab Emirates)"},
        {"ar-QA", "Arabic (Qatar)"},
        {"ar-KW", "Arabic (Kuwait)"},
        {"ar-BH", "Arabic (Bahrain)"},
        {"ar-OM", "Arabic (Oman)"},
        {"ar-JO", "Arabic (Jordan)"},
        {"ar-LB", "Arabic (Lebanon)"},
        {"ar-SY", "Arabic (Syria)"},
        {"ar-IQ", "Arabic (Iraq)"},
        {"ar-YE", "Arabic (Yemen)"},
        {"ar-LY", "Arabic (Libya)"},
        {"ar-TN", "Arabic (Tunisia)"},
        {"ar-DZ", "Arabic (Algeria)"},
        {"ar-MA", "Arabic (Morocco)"},
        
        // Major world languages
        {"zh-CN", "Chinese (Mandarin, Simplified)"},
        {"zh-TW", "Chinese (Taiwanese Mandarin, Traditional)"},
        {"zh-HK", "Chinese (Cantonese, Traditional)"},
        {"hi-IN", "Hindi (India)"},
        {"es-ES", "Spanish (Spain)"},
        {"es-MX", "Spanish (Mexico)"},
        {"es-US", "Spanish (United States)"},
        {"fr-FR", "French (France)"},
        {"fr-CA", "French (Canada)"},
        {"de-DE", "German (Germany)"},
        {"it-IT", "Italian (Italy)"},
        {"ja-JP", "Japanese (Japan)"},
        {"ko-KR", "Korean (Korea)"},
        {"pt-BR", "Portuguese (Brazil)"},
        {"pt-PT", "Portuguese (Portugal)"},
        {"ru-RU", "Russian (Russia)"},
        
        // Additional Azure supported languages
        {"nl-NL", "Dutch (Netherlands)"},
        {"sv-SE", "Swedish (Sweden)"},
        {"da-DK", "Danish (Denmark)"},
        {"nb-NO", "Norwegian (Norway)"},
        {"fi-FI", "Finnish (Finland)"},
        {"pl-PL", "Polish (Poland)"},
        {"cs-CZ", "Czech (Czech Republic)"},
        {"hu-HU", "Hungarian (Hungary)"},
        {"tr-TR", "Turkish (Turkey)"},
        {"th-TH", "Thai (Thailand)"},
        {"vi-VN", "Vietnamese (Vietnam)"},
        {"id-ID", "Indonesian (Indonesia)"},
        {"ms-MY", "Malay (Malaysia)"},
        {"ta-IN", "Tamil (India)"},
        {"te-IN", "Telugu (India)"},
        {"kn-IN", "Kannada (India)"},
        {"ml-IN", "Malayalam (India)"},
        {"gu-IN", "Gujarati (India)"},
        {"mr-IN", "Marathi (India)"},
        {"bn-IN", "Bengali (India)"},
        {"pa-IN", "Punjabi (India)"}
    };

    public AzureStreamingSTTService(IOptions<ServiceOptions> options, ILogger<AzureStreamingSTTService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Process audio stream with the specified language for transcription
    /// </summary>
    public async IAsyncEnumerable<TranscriptionResult> ProcessStreamAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌍 Azure STT Processing for language: {Language}", language);
        Console.WriteLine($"🌍 AZURE STT: Starting processing for language: {language}");
        
        await foreach (var result in ProcessAzureStreamingAsync(audioStream, language, cancellationToken))
        {
            yield return result;
        }
    }

    // Replace the ProcessAzureStreamingAsync method in your AzureStreamingSTTService.cs:

    private async IAsyncEnumerable<TranscriptionResult> ProcessAzureStreamingAsync(
        ChannelReader<byte[]> audioStream,
        string language,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {

        var mappedLanguage = MapToAzureLanguageCode(language);
        Console.WriteLine($"🔵 DEBUG: Azure STT Config - Mapped Language: {mappedLanguage}");
        
        var speechConfig = SpeechConfig.FromSubscription(_options.Azure!.SpeechKey!, _options.Azure.SpeechRegion ?? "eastus");
        speechConfig.SpeechRecognitionLanguage = mappedLanguage;
        
        Console.WriteLine($"🔵 DEBUG: Azure STT Config - Final SpeechConfig Language: {speechConfig.SpeechRecognitionLanguage}");
        Console.WriteLine($"🔵 DEBUG: Azure STT Config - Final SpeechConfig Region: {speechConfig.Region}");
        
        speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "5000");
        speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "3000");

        var pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
        var audioConfig = AudioConfig.FromStreamInput(pushStream);
        
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
        
        var resultChannel = Channel.CreateUnbounded<TranscriptionResult>();
        var completionSource = new TaskCompletionSource<bool>();
        var hasError = false;

        // 🚀 Optimization: Removed manual "OPTIMAL_CHUNK_SIZE" buffer.
        // Azure SDK buffers internally. Sending chunks as valid PCM frames allows faster transmission.

        recognizer.Recognizing += (s, e) =>
        {
            Console.WriteLine($"🔥 AZURE EVENT: Recognizing event fired! Reason: {e.Result.Reason}, Language: {language}");
            Console.WriteLine($"   📝 Text: '{e.Result.Text}'");
            Console.WriteLine($"   📊 Duration: {e.Result.Duration.TotalMilliseconds}ms, Offset: {e.Result.OffsetInTicks / 10000}ms");
            
            if (!string.IsNullOrWhiteSpace(e.Result.Text))
            {
                // 🎤 INTERIM RESULT
                Console.WriteLine($"🔄 Azure STT Interim ({language}): \"{e.Result.Text}\"");
                _logger.LogDebug("🔄 Azure Interim: {Text}", e.Result.Text);
                resultChannel.Writer.TryWrite(new TranscriptionResult
                {
                    Text = e.Result.Text,
                    IsFinal = false,
                    Language = language,
                    Confidence = 0.5, // Azure doesn't provide confidence for interim
                    Timestamp = TimeSpan.FromTicks(e.Result.OffsetInTicks),
                    Duration = e.Result.Duration
                });
            }
            else
            {
                Console.WriteLine($"🔄 Azure STT Interim ({language}): [EMPTY RESULT] - Reason: {e.Result.Reason}");
            }
        };

        recognizer.Recognized += (s, e) =>
        {
            Console.WriteLine($"🔥 AZURE EVENT: Recognized event fired! Reason: {e.Result.Reason}, Language: {language}");
            Console.WriteLine($"   📝 Text: '{e.Result.Text}'");
            Console.WriteLine($"   📊 Duration: {e.Result.Duration.TotalMilliseconds}ms, Offset: {e.Result.OffsetInTicks / 10000}ms");
            Console.WriteLine($"   🎯 Result ID: {e.Result.ResultId}");
            
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                // ✅ FINAL RESULT - complete utterance recognized
                var confidence = CalculateConfidence(e.Result);
                Console.WriteLine($"✅ Azure STT FINAL ({language}): \"{e.Result.Text}\" [Confidence: {confidence:P1}]");
                Console.WriteLine($"   📊 Duration: {e.Result.Duration.TotalMilliseconds:F0}ms | Offset: {e.Result.OffsetInTicks / 10000}ms");
                Console.WriteLine($"   🌍 Configured Language: {speechConfig.SpeechRecognitionLanguage}");
                Console.WriteLine($"   🎯 Result ID: {e.Result.ResultId}");
                
                _logger.LogInformation("✅ Azure STT Final Result for {Language}: \"{Text}\" (Confidence: {Confidence:P1}, Duration: {Duration}ms)", 
                    language, e.Result.Text, confidence, e.Result.Duration.TotalMilliseconds);

                var result = new TranscriptionResult
                {
                    Text = e.Result.Text,
                    IsFinal = true,
                    Language = language,
                    Confidence = confidence,
                    Timestamp = TimeSpan.FromTicks(e.Result.OffsetInTicks),
                    Duration = e.Result.Duration
                };

                resultChannel.Writer.TryWrite(result);
                Console.WriteLine($"✅ Azure STT: Transcription result sent to channel for {language}");
            }
            else if (e.Result.Reason == ResultReason.NoMatch)
            {
                Console.WriteLine($"⚠️  Azure STT No Match ({language}): Could not recognize speech in audio chunk");
                Console.WriteLine($"   📝 Details: {e.Result.ToString()}");
                Console.WriteLine($"   🌍 Configured Language: {speechConfig.SpeechRecognitionLanguage}");
                _logger.LogWarning("⚠️  Azure STT No Match for {Language}: {Details}", language, e.Result.ToString());
            }
            else
            {
                Console.WriteLine($"❓ Azure STT Unexpected Result ({language}): Reason={e.Result.Reason}, Text='{e.Result.Text}'");
            }
        };

        recognizer.Canceled += (s, e) =>
        {
            var details = CancellationDetails.FromResult(e.Result);
            Console.WriteLine($"🔥 AZURE EVENT: Canceled event fired! Reason: {details.Reason}, Language: {language}");
            Console.WriteLine($"❌ Azure STT Canceled ({language}): {details.Reason}");
            
            if (details.Reason == CancellationReason.Error)
            {
                Console.WriteLine($"   🚨 Error Code: {details.ErrorCode}");
                Console.WriteLine($"   📝 Error Details: {details.ErrorDetails}");
                Console.WriteLine($"   🌍 Configured Language: {speechConfig.SpeechRecognitionLanguage}");
                hasError = true;
                
                _logger.LogWarning("❌ Azure Error: {Code} - {Details}", details.ErrorCode, details.ErrorDetails);
                
                // Signal error downstream cleanly
                resultChannel.Writer.TryWrite(new TranscriptionResult 
                { 
                    Text = "[Azure Error]", 
                    IsFinal = true, 
                    Confidence = 0 
                });
            }
            else if (details.Reason == CancellationReason.EndOfStream)
            {
                Console.WriteLine($"✅ Azure EndOfStream reached for {language}");
                _logger.LogInformation("✅ Azure EndOfStream reached for {Language}", language);
            }
            
            // Only complete success if NOT an error
            completionSource.TrySetResult(!hasError);
        };

        recognizer.SessionStopped += (s, e) =>
        {
            Console.WriteLine($"� AZURE EVENT: SessionStopped event fired! Language: {language}");
            Console.WriteLine($"�🛑 Azure STT Session Stopped ({language})");
            _logger.LogInformation("🛑 Azure STT session stopped for {Language}", language);
            resultChannel.Writer.TryComplete();
            completionSource.TrySetResult(true);
        };

        // 1. Setup and Start Azure
        try
        {
            await recognizer.StartContinuousRecognitionAsync();
            _logger.LogInformation("🎤 Started Azure continuous recognition for {Language}", language);

            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("🔵 Azure STT: Starting Audio Pump with Chunk Accumulator for {Language}", language);
                    
                    // 🎯 Azure STT Chunk Requirements: 1600-6400 bytes optimal
                    const int AZURE_MIN_CHUNK_SIZE = 1600;
                    const int AZURE_MAX_CHUNK_SIZE = 6400;
                    const int AZURE_TARGET_CHUNK_SIZE = 3200; // Sweet spot for Azure
                    
                    var accumulatedBuffer = new List<byte>();
                    var chunkCount = 0;
                    var azureChunkCount = 0;
                    
                    Console.WriteLine($"🔵 Azure Accumulator: Target chunk size: {AZURE_TARGET_CHUNK_SIZE} bytes (Min: {AZURE_MIN_CHUNK_SIZE}, Max: {AZURE_MAX_CHUNK_SIZE})");
                    
                    await foreach (var audioChunk in audioStream.ReadAllAsync(cancellationToken))
                    {
                        if (audioChunk != null && audioChunk.Length > 0)
                        {
                            chunkCount++;
                            accumulatedBuffer.AddRange(audioChunk);
                            
                            Console.WriteLine($"🔵 Azure Accumulator: Received chunk #{chunkCount} ({audioChunk.Length} bytes) - Buffer now: {accumulatedBuffer.Count} bytes");
                            
                            // 🎯 Send to Azure when we have enough data
                            if (accumulatedBuffer.Count >= AZURE_TARGET_CHUNK_SIZE)
                            {
                                // Create optimal Azure chunk
                                var azureChunk = accumulatedBuffer.Take(AZURE_TARGET_CHUNK_SIZE).ToArray();
                                accumulatedBuffer.RemoveRange(0, AZURE_TARGET_CHUNK_SIZE);
                                
                                azureChunkCount++;
                                Console.WriteLine($"✅ Azure Accumulator: Sending optimized chunk #{azureChunkCount} ({azureChunk.Length} bytes) to Azure STT");
                                Console.WriteLine($"🎤 AZURE DEBUG: About to call pushStream.Write with {azureChunk.Length} bytes");
                                Console.WriteLine($"   📊 First 10 bytes: [{string.Join(", ", azureChunk.Take(10))}]");
                                
                                pushStream.Write(azureChunk);
                                Console.WriteLine($"✅ AZURE DEBUG: pushStream.Write completed successfully for chunk #{azureChunkCount}");
                            }
                            // 🚨 Emergency send if buffer gets too large (prevent memory buildup)
                            else if (accumulatedBuffer.Count >= AZURE_MAX_CHUNK_SIZE)
                            {
                                var azureChunk = accumulatedBuffer.Take(AZURE_MAX_CHUNK_SIZE).ToArray();
                                accumulatedBuffer.RemoveRange(0, AZURE_MAX_CHUNK_SIZE);
                                
                                azureChunkCount++;
                                Console.WriteLine($"� Azure Accumulator: Emergency flush - sending large chunk #{azureChunkCount} ({azureChunk.Length} bytes)");
                                
                                pushStream.Write(azureChunk);
                            }
                        }
                    }
                    
                    // 🏁 Final flush - send remaining data even if smaller than target
                    if (accumulatedBuffer.Count >= AZURE_MIN_CHUNK_SIZE)
                    {
                        var finalChunk = accumulatedBuffer.ToArray();
                        azureChunkCount++;
                        Console.WriteLine($"🏁 Azure Accumulator: Final flush - sending remaining chunk #{azureChunkCount} ({finalChunk.Length} bytes)");
                        pushStream.Write(finalChunk);
                    }
                    else if (accumulatedBuffer.Count > 0)
                    {
                        Console.WriteLine($"⚠️ Azure Accumulator: Discarding final small chunk ({accumulatedBuffer.Count} bytes) - too small for Azure STT");
                    }
                    
                    _logger.LogInformation("✅ Azure STT: Audio Pump Completed for {Language} - Processed {InputChunks} input chunks → {AzureChunks} Azure-optimized chunks", 
                        language, chunkCount, azureChunkCount);
                    
                    Console.WriteLine($"📊 Azure Accumulator Summary: {chunkCount} input chunks → {azureChunkCount} optimized Azure chunks");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Azure STT: Audio Pump Failed for {Language}", language);
                    Console.WriteLine($"❌ Azure Accumulator Error: {ex.Message}");
                    hasError = true;
                }
                finally
                {
                    pushStream.Close();
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to start Azure STT for {Language}", language);
            resultChannel.Writer.TryComplete();
        }

        // 2. Yield results (Outside try-catch to satisfy C# compiler)
        await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return result;
        }

        // 3. Cleanup
        try
        {
            await Task.WhenAny(completionSource.Task, Task.Delay(1000, cancellationToken));
            await recognizer.StopContinuousRecognitionAsync();
        }
        catch { /* Ignore cleanup errors */ }
    }

    /// <summary>
    /// Map generic language codes to Azure-specific codes
    /// </summary>
    private string MapToAzureLanguageCode(string language)
    {
        // Ensure we have a valid Azure language code
        return AzureSTTLanguages.ContainsKey(language) ? language : "en-US";
    }

    /// <summary>
    /// Extract confidence from Azure recognition result
    /// </summary>
    private double CalculateConfidence(SpeechRecognitionResult result)
    {
        // Azure doesn't directly provide confidence in basic results
        // You can enhance this based on result properties
        return result.Reason switch
        {
            ResultReason.RecognizedSpeech => 0.85,
            ResultReason.NoMatch => 0.0,
            _ => 0.5
        };
    }
}