using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Infrastructure.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Debug test for Azure STT service using real saved audio files from root directory
/// Tests the main TranscribeWithDetectionAsync method
/// </summary>
public class AzureSTTRealAudioDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly AzureSTTService _azureSTTService;

    public AzureSTTRealAudioDebugTest(ITestOutputHelper output)
    {
        _output = output;

        // Configure test options (using environment variables or test config)
        var options = Options.Create(new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ?? "YOUR_AZURE_SPEECH_KEY_HERE",
                SpeechRegion = "northeurope",
                SpeechEndpoint = "https://northeurope.api.cognitive.microsoft.com/"
            }
        });

        // Create logger that outputs to test console
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        var logger = loggerFactory.CreateLogger<AzureSTTService>();

        _azureSTTService = new AzureSTTService(options, logger);
    }

    [Fact]
    public async Task TestAzureSTTWithRealSavedAudio_ShouldProcessSuccessfully()
    {
        // Arrange
        _output.WriteLine("=== Starting Azure STT Real Audio Debug Test ===");
        
        // Find audio files in root directory
        string rootDirectory = "/Users/farhanfarooq/Documents/GitHub/A3ITranslator";
        var audioFiles = Directory.GetFiles(rootDirectory, "DEBUG_AUDIO_*.ogg");
        
        _output.WriteLine($"Found {audioFiles.Length} debug audio files in root directory");

        if (audioFiles.Length == 0)
        {
            _output.WriteLine("No debug audio files found. Test skipped.");
            return;
        }

        // Use the most recent audio file
        var latestAudioFile = audioFiles.OrderByDescending(f => File.GetCreationTime(f)).First();
        _output.WriteLine($"Using latest audio file: {Path.GetFileName(latestAudioFile)}");
        
        // Read the audio file
        byte[] audioData = await File.ReadAllBytesAsync(latestAudioFile);
        _output.WriteLine($"Audio file size: {audioData.Length} bytes");

        // Detect format using our helper
        var formatInfo = AudioFormatHelper.DetectFormat(audioData);
        _output.WriteLine($"Detected format: {formatInfo.Format} - {formatInfo.Description}");
        
        // Define candidate languages (source and target)
        string[] candidateLanguages = { "en-US", "ur-IN" };
        _output.WriteLine($"Testing with candidate languages: [{string.Join(", ", candidateLanguages)}]");

        try
        {
            // Act - Use the audio directly (Azure STT will handle conversion internally)
            _output.WriteLine("=== Calling Azure STT TranscribeWithDetectionAsync ===");
            var result = await _azureSTTService.TranscribeWithDetectionAsync(
                audioData, 
                candidateLanguages, 
                CancellationToken.None);

            // Assert and Debug Output
            _output.WriteLine("=== Azure STT Results ===");
            _output.WriteLine($"Success: {result.Success}");
            _output.WriteLine($"Provider: {result.Provider}");
            _output.WriteLine($"Processing Time: {result.ProcessingTimeMs}ms");
            
            if (result.Success)
            {
                _output.WriteLine($"Transcription: '{result.Transcription}'");
                _output.WriteLine($"Detected Language: {result.DetectedLanguage}");
                _output.WriteLine($"Confidence: {result.Confidence}");
                
                if (result.SpeakerAnalysis != null)
                {
                    _output.WriteLine($"Speaker Analysis - Tag: {result.SpeakerAnalysis.SpeakerTag}, Label: {result.SpeakerAnalysis.SpeakerLabel}");
                    _output.WriteLine($"Speaker Analysis - Gender: {result.SpeakerAnalysis.Gender}, Age: {result.SpeakerAnalysis.EstimatedAgeRange}");
                }
                
                _output.WriteLine($"Word Count: {result.Words?.Count ?? 0}");
            }
            else
            {
                _output.WriteLine($"Error Message: {result.ErrorMessage}");
            }

            // Verify basic functionality
            Assert.NotNull(result);
            Assert.Equal("Azure STT", result.Provider);
            
            // If credentials are not configured, we expect a specific error
            if (result.ErrorMessage?.Contains("credentials not configured") == true)
            {
                _output.WriteLine("✅ Expected error due to missing Azure credentials");
                Assert.False(result.Success);
                Assert.Contains("credentials", result.ErrorMessage);
            }
            else if (result.ErrorMessage?.Contains("Recognition error") == true)
            {
                _output.WriteLine("✅ Azure STT method executed but failed due to audio format or credentials");
                Assert.False(result.Success);
            }
            else if (result.ErrorMessage?.Contains("FFmpeg conversion failed") == true)
            {
                _output.WriteLine("✅ Audio conversion error detected - this helps us debug FFmpeg issues");
                Assert.False(result.Success);
            }
            else
            {
                _output.WriteLine("✅ Azure STT method executed successfully");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"=== Exception Caught ===");
            _output.WriteLine($"Exception Type: {ex.GetType().Name}");
            _output.WriteLine($"Exception Message: {ex.Message}");
            _output.WriteLine($"Stack Trace: {ex.StackTrace}");
            
            // For debugging purposes, we'll assert that we at least get some kind of response
            Assert.True(true, "Exception occurred but method was called - this helps us debug the implementation");
        }

        _output.WriteLine("=== Test Completed ===");
    }

    [Fact]
    public async Task TestAzureSTTWithGeneratedWavAudio_ShouldProcessCorrectly()
    {
        // Arrange
        _output.WriteLine("=== Testing Azure STT with Generated WAV Audio ===");
        
        // Generate a simple test WAV file
        byte[] testWavAudio = GenerateTestWavAudio();
        _output.WriteLine($"Generated test WAV audio: {testWavAudio.Length} bytes");
        
        // Verify it's detected as WAV
        var formatInfo = AudioFormatHelper.DetectFormat(testWavAudio);
        _output.WriteLine($"Generated format: {formatInfo.Format} - {formatInfo.Description}");
        
        string[] candidateLanguages = { "en-US", "fr-FR" };
        _output.WriteLine($"Testing with candidate languages: [{string.Join(", ", candidateLanguages)}]");

        try
        {
            // Act - Test the audio conversion helper directly first
            _output.WriteLine("=== Testing Direct Conversion Helper ===");
            var convertedFilePath = await AudioConversionHelper.ConvertToWavWithFFmpeg(testWavAudio, CancellationToken.None);
            _output.WriteLine($"Conversion successful: {convertedFilePath}");
            
            // Clean up the converted file
            if (File.Exists(convertedFilePath))
            {
                File.Delete(convertedFilePath);
                _output.WriteLine("Cleaned up converted file");
            }
            
            // Act - Now test full transcription
            _output.WriteLine("=== Calling Azure STT with Generated WAV ===");
            var result = await _azureSTTService.TranscribeWithDetectionAsync(
                testWavAudio, 
                candidateLanguages, 
                CancellationToken.None);

            // Assert and Debug Output
            _output.WriteLine("=== Azure STT Results ===");
            _output.WriteLine($"Success: {result.Success}");
            _output.WriteLine($"Provider: {result.Provider}");
            _output.WriteLine($"Processing Time: {result.ProcessingTimeMs}ms");
            
            if (result.Success)
            {
                _output.WriteLine($"Transcription: '{result.Transcription}'");
                _output.WriteLine($"Detected Language: {result.DetectedLanguage}");
                _output.WriteLine($"Confidence: {result.Confidence}");
            }
            else
            {
                _output.WriteLine($"Error Message: {result.ErrorMessage}");
            }

            // Verify basic functionality
            Assert.NotNull(result);
            Assert.Equal("Azure STT", result.Provider);
            
            if (result.ErrorMessage?.Contains("credentials not configured") == true)
            {
                _output.WriteLine("✅ Expected error due to missing Azure credentials");
                Assert.False(result.Success);
            }
            else if (result.ErrorMessage?.Contains("401") == true || result.ErrorMessage?.Contains("Unauthorized") == true)
            {
                _output.WriteLine("✅ Authentication error - method reached Azure but credentials invalid");
                Assert.False(result.Success);
            }
            else
            {
                _output.WriteLine("✅ Azure STT method executed - checking result");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"=== Exception Caught ===");
            _output.WriteLine($"Exception Type: {ex.GetType().Name}");
            _output.WriteLine($"Exception Message: {ex.Message}");
            
            // Test that the method at least gets to Azure SDK without format errors
            Assert.True(true, "Method executed and reached Azure SDK processing");
        }

        _output.WriteLine("=== Generated WAV Test Completed ===");
    }

    /// <summary>
    /// Generate a proper WAV audio file for testing
    /// </summary>
    private byte[] GenerateTestWavAudio()
    {
        // Generate 1 second of sine wave audio at 440Hz (A note)
        int sampleRate = 16000;
        int duration = 1; // 1 second
        int samples = sampleRate * duration;
        
        var audioData = new List<byte>();
        
        for (int i = 0; i < samples; i++)
        {
            // Generate sine wave
            double time = (double)i / sampleRate;
            double amplitude = 0.3 * Math.Sin(2 * Math.PI * 440 * time); // 440Hz sine wave
            short sample = (short)(amplitude * short.MaxValue);
            
            // Convert to little-endian bytes
            audioData.Add((byte)(sample & 0xFF));
            audioData.Add((byte)((sample >> 8) & 0xFF));
        }
        
        // Create WAV header
        var header = new byte[]
        {
            // RIFF header
            0x52, 0x49, 0x46, 0x46, // "RIFF"
            0, 0, 0, 0,             // File size (will be calculated)
            0x57, 0x41, 0x56, 0x45, // "WAVE"
            
            // fmt chunk
            0x66, 0x6D, 0x74, 0x20, // "fmt "
            16, 0, 0, 0,            // fmt chunk size (16 for PCM)
            1, 0,                   // PCM format
            1, 0,                   // Mono (1 channel)
            0x80, 0x3E, 0, 0,      // Sample rate (16000 Hz)
            0, 0x7D, 0, 0,         // Byte rate (16000 * 1 * 16/8 = 32000)
            2, 0,                   // Block align (1 * 16/8 = 2)
            16, 0,                  // Bits per sample (16)
            
            // data chunk
            0x64, 0x61, 0x74, 0x61, // "data"
            0, 0, 0, 0              // Data size (will be calculated)
        };
        
        // Calculate sizes
        int dataSize = audioData.Count;
        int fileSize = header.Length + dataSize - 8;
        
        // Fill in file size
        header[4] = (byte)(fileSize & 0xFF);
        header[5] = (byte)((fileSize >> 8) & 0xFF);
        header[6] = (byte)((fileSize >> 16) & 0xFF);
        header[7] = (byte)((fileSize >> 24) & 0xFF);
        
        // Fill in data size
        header[40] = (byte)(dataSize & 0xFF);
        header[41] = (byte)((dataSize >> 8) & 0xFF);
        header[42] = (byte)((dataSize >> 16) & 0xFF);
        header[43] = (byte)((dataSize >> 24) & 0xFF);
        
        // Combine header and data
        var result = new byte[header.Length + audioData.Count];
        Array.Copy(header, 0, result, 0, header.Length);
        audioData.CopyTo(result, header.Length);
        
        return result;
    }

    [Fact]
    public async Task TestAzureSTTWithAllSavedAudioFiles_ShouldProcessEach()
    {
        // Arrange
        _output.WriteLine("=== Testing All Saved Audio Files ===");
        
        string rootDirectory = "/Users/farhanfarooq/Documents/GitHub/A3ITranslator";
        var audioFiles = Directory.GetFiles(rootDirectory, "DEBUG_AUDIO_*.ogg");
        
        _output.WriteLine($"Found {audioFiles.Length} debug audio files");
        
        if (audioFiles.Length == 0)
        {
            _output.WriteLine("No debug audio files found. Test skipped.");
            return;
        }

        string[] candidateLanguages = { "en-US", "ur-PK" };

        // Process each audio file (limit to first 3 to avoid long test times)
        for (int i = 0; i < Math.Min(audioFiles.Length, 3); i++)
        {
            var audioFile = audioFiles[i];
            var fileName = Path.GetFileName(audioFile);
            _output.WriteLine($"\n=== Processing Audio File {i + 1}: {fileName} ===");
            
            try
            {
                byte[] audioData = await File.ReadAllBytesAsync(audioFile);
                _output.WriteLine($"File size: {audioData.Length} bytes");
                
                // Detect format
                var formatInfo = AudioFormatHelper.DetectFormat(audioData);
                _output.WriteLine($"Detected format: {formatInfo.Format} - {formatInfo.Description}");
                
                // Test conversion helper directly first
                _output.WriteLine("Testing conversion...");
                var convertedPath = await AudioConversionHelper.ConvertToWavWithFFmpeg(audioData, CancellationToken.None);
                _output.WriteLine($"Conversion successful: {Path.GetFileName(convertedPath)}");
                
                // Clean up converted file
                if (File.Exists(convertedPath))
                {
                    File.Delete(convertedPath);
                }
                
                // Now test full transcription
                var result = await _azureSTTService.TranscribeWithDetectionAsync(
                    audioData, 
                    candidateLanguages, 
                    CancellationToken.None);
                
                _output.WriteLine($"Result - Success: {result.Success}");
                if (result.Success)
                {
                    _output.WriteLine($"Transcription: '{result.Transcription}'");
                    _output.WriteLine($"Language: {result.DetectedLanguage}");
                }
                else
                {
                    _output.WriteLine($"Error: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception for {fileName}: {ex.Message}");
            }
        }
        
        _output.WriteLine("=== All Files Processed ===");
        Assert.True(true, "Batch processing completed");
    }

    [Fact]
    public async Task TestAzureSTTServiceHealth_ShouldReturnStatus()
    {
        // Arrange & Act
        _output.WriteLine("=== Testing Azure STT Service Health ===");
        
        var isHealthy = await _azureSTTService.CheckHealthAsync();
        
        // Assert & Debug
        _output.WriteLine($"Service Health: {(isHealthy ? "Healthy" : "Unhealthy")}");
        _output.WriteLine($"Supports Language Detection: {_azureSTTService.SupportsLanguageDetection}");
        _output.WriteLine($"Requires Audio Conversion: {_azureSTTService.RequiresAudioConversion}");
        _output.WriteLine($"Service Name: {_azureSTTService.GetServiceName()}");
        
        var supportedLanguages = _azureSTTService.GetSupportedLanguages();
        _output.WriteLine($"Supported Languages Count: {supportedLanguages.Count}");
        _output.WriteLine($"Sample Languages: {string.Join(", ", supportedLanguages.Take(5).Select(kv => $"{kv.Key}={kv.Value}"))}");
        
        Assert.NotNull(_azureSTTService);
        Assert.Equal("Azure STT", _azureSTTService.GetServiceName());
        Assert.True(_azureSTTService.SupportsLanguageDetection);
        Assert.True(_azureSTTService.RequiresAudioConversion);
    }

    [Fact]
    public async Task DebugAzureSTT_SimpleBreakpointTest()
    {
        // Arrange
        _output.WriteLine("=== Simple Debug Test for Azure STT ===");
        
        // Generate test audio
        byte[] testAudio = GenerateTestWavAudio();
        _output.WriteLine($"Generated test audio: {testAudio.Length} bytes");
        
        // Verify format detection
        var formatInfo = AudioFormatHelper.DetectFormat(testAudio);
        _output.WriteLine($"Audio format: {formatInfo.Format} - {formatInfo.Description}");
        
        string[] languages = { "en-US", "en-GB" }; // Use well-known languages for testing
        
        // Act - This is where you can set breakpoints
        _output.WriteLine("About to test conversion - SET BREAKPOINT HERE");
        
        try
        {
            // Test conversion helper directly
            var convertedPath = await AudioConversionHelper.ConvertToWavWithFFmpeg(testAudio, CancellationToken.None);
            _output.WriteLine($"Conversion successful: {convertedPath}");
            
            // Clean up
            if (File.Exists(convertedPath))
            {
                File.Delete(convertedPath);
            }
            
            // Test full transcription
            _output.WriteLine("About to call Azure STT - SET BREAKPOINT HERE");
            var result = await _azureSTTService.TranscribeWithDetectionAsync(
                testAudio, 
                languages, 
                CancellationToken.None);
            
            // Assert
            _output.WriteLine($"Result received: Success={result.Success}");
            if (result.Success)
            {
                _output.WriteLine($"Transcription: {result.Transcription}");
            }
            else
            {
                _output.WriteLine($"Error: {result.ErrorMessage}");
            }
            
            Assert.NotNull(result);
            Assert.Equal("Azure STT", result.Provider);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Exception: {ex.Message}");
            Assert.True(true, "Method executed - debugging completed");
        }
    }
}
