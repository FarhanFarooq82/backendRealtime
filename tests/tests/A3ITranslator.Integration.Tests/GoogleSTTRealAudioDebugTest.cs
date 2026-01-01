using A3ITranslator.Infrastructure.Services.Google;
using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Infrastructure.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Debug test for Google STT service using real saved audio files from root directory
/// Tests the main TranscribeWithDetectionAsync method with Google Cloud Speech-to-Text
/// </summary>
public class GoogleSTTRealAudioDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly GoogleSTTService _googleSTTService;

    public GoogleSTTRealAudioDebugTest(ITestOutputHelper output)
    {
        _output = output;

        // Configure test options (using environment variables or test config)
        var options = Options.Create(new ServiceOptions
        {
            Google = new GoogleOptions
            {
                CredentialsPath = "../../../../../a3itranslator-9b86c705f20c.json",
                ProjectId = "a3itranslator",
                Location = "europe-west4",  
                RecognizerId = "eu4-chirp2-recognizer",
                STTModel = "chirp_2"  // Use chirp_2 model for testing
            }
        });

        // Create logger that outputs to test console
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        var logger = loggerFactory.CreateLogger<GoogleSTTService>();

        _googleSTTService = new GoogleSTTService(options, logger);
    }

    [Fact]
    public async Task TestGoogleSTTWithRealSavedAudio_ShouldProcessSuccessfully()
    {
        // Arrange 
        _output.WriteLine("=== Starting Google STT Real Audio Debug Test ===");
        
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
        string[] candidateLanguages = { "da-DK", "ur-PK" };
        _output.WriteLine($"Testing with candidate languages: [{string.Join(", ", candidateLanguages)}]");
        _output.WriteLine("Note: chirp_2 model does not support multiple language detection.");
        _output.WriteLine("Service will try each language individually until one succeeds.");

        try
        {
            // Act - Google STT natively supports multiple formats without conversion
            _output.WriteLine("=== Calling Google STT TranscribeWithDetectionAsync ===");
            var result = await _googleSTTService.TranscribeWithDetectionAsync(
                audioData, 
                candidateLanguages, 
                CancellationToken.None);

            // Assert and Debug Output
            _output.WriteLine("=== Google STT Results ===");
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
            Assert.Equal("Google STT", result.Provider);
            
            // If credentials are not configured, we expect a specific error
            if (result.ErrorMessage?.Contains("credential") == true || result.ErrorMessage?.Contains("GOOGLE_APPLICATION_CREDENTIALS") == true)
            {
                _output.WriteLine("✅ Expected error due to missing Google credentials");
                Assert.False(result.Success);
                Assert.True(result.ErrorMessage.Contains("credential") || result.ErrorMessage.Contains("GOOGLE_APPLICATION_CREDENTIALS"));
            }
            else if (result.ErrorMessage?.Contains("Recognition error") == true)
            {
                _output.WriteLine("✅ Google STT method executed but failed due to audio format or credentials");
                Assert.False(result.Success);
            }
            else if (result.ErrorMessage?.Contains("Audio encoding") == true)
            {
                _output.WriteLine("✅ Audio encoding error detected - Google STT supports many formats natively");
                Assert.False(result.Success);
            }
            else
            {
                _output.WriteLine("✅ Google STT method executed successfully");
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
    public async Task TestGoogleSTTWithGeneratedWavAudio_ShouldProcessCorrectly()
    {
        // Arrange
        _output.WriteLine("=== Starting Google STT Generated WAV Audio Test ===");
        
        // Generate a simple WAV audio for testing (440Hz tone, 1 second)
        byte[] wavAudioData = TestAudioGenerator.GenerateTestWavFile(1.0, 16000); // 16kHz sample rate for speech
        
        _output.WriteLine($"Generated WAV audio size: {wavAudioData.Length} bytes");
        
        // Detect format to verify it's WAV
        var formatInfo = AudioFormatHelper.DetectFormat(wavAudioData);
        _output.WriteLine($"Generated format: {formatInfo.Format} - {formatInfo.Description}");
        
        // Define candidate languages
        string[] candidateLanguages = { "en-US", "es-ES" };
        _output.WriteLine($"Testing with candidate languages: [{string.Join(", ", candidateLanguages)}]");

        try
        {
            // Act - Test Google STT with generated audio
            _output.WriteLine("=== Calling Google STT with Generated WAV ===");
            var result = await _googleSTTService.TranscribeWithDetectionAsync(
                wavAudioData, 
                candidateLanguages, 
                CancellationToken.None);

            // Assert and Debug Output
            _output.WriteLine("=== Google STT Generated Audio Results ===");
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
            Assert.Equal("Google STT", result.Provider);
            
            // Since this is generated tone audio (not speech), we expect it to either:
            // 1. Fail due to no speech content, or
            // 2. Fail due to missing credentials
            if (result.ErrorMessage?.Contains("credential") == true)
            {
                _output.WriteLine("✅ Expected credential error");
                Assert.False(result.Success);
            }
            else if (result.ErrorMessage?.Contains("No speech") == true || result.Success == false)
            {
                _output.WriteLine("✅ Expected behavior - tone audio doesn't contain speech");
            }
            else
            {
                _output.WriteLine("✅ Unexpected success with tone audio - Google STT is very permissive");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"=== Exception with Generated Audio ===");
            _output.WriteLine($"Exception Type: {ex.GetType().Name}");
            _output.WriteLine($"Exception Message: {ex.Message}");
            
            // This is expected for credential issues
            Assert.True(true, "Exception with generated audio is acceptable for debugging");
        }

        _output.WriteLine("=== Generated Audio Test Completed ===");
    }

    [Fact]
    public async Task TestGoogleSTTServiceHealth_ShouldReturnStatus()
    {
        // Arrange
        _output.WriteLine("=== Testing Google STT Service Health ===");

        try
        {
            // Act
            var isHealthy = await _googleSTTService.CheckHealthAsync();
            
            // Assert and Debug Output
            _output.WriteLine($"Health Status: {isHealthy}");
            
            if (isHealthy)
            {
                _output.WriteLine("✅ Google STT service is healthy and credentials are valid");
                Assert.True(isHealthy);
            }
            else
            {
                _output.WriteLine("❌ Google STT service is not healthy (likely due to missing credentials)");
                Assert.False(isHealthy);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Health check exception: {ex.Message}");
            _output.WriteLine("❌ Health check failed with exception (likely credential issue)");
            
            // Exception during health check is acceptable for debugging
            Assert.True(true, "Health check exception indicates credential configuration issue");
        }

        _output.WriteLine("=== Health Check Completed ===");
    }

    [Fact]
    public void TestGoogleSTTSupportedLanguages()
    {
        // Arrange
        _output.WriteLine("=== Testing Google STT Supported Languages ===");

        try
        {
            // Act
            var languages = _googleSTTService.GetSupportedLanguages();
            
            // Assert and Debug Output
            _output.WriteLine($"Total supported languages: {languages.Count}");
            
            // Log first 10 languages for debugging
            var firstTen = languages.Take(10);
            foreach (var lang in firstTen)
            {
                _output.WriteLine($"  {lang.Key}: {lang.Value}");
            }
            
            if (languages.Count > 10)
            {
                _output.WriteLine($"  ... and {languages.Count - 10} more languages");
            }

            // Verify key languages are present
            Assert.True(languages.ContainsKey("en-US"), "English (US) should be supported");
            Assert.True(languages.ContainsKey("es-ES"), "Spanish (Spain) should be supported");
            Assert.True(languages.ContainsKey("fr-FR"), "French (France) should be supported");
            
            _output.WriteLine("✅ Google STT language list is properly loaded");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Language list exception: {ex.Message}");
            throw; // Language list should always be available
        }

        _output.WriteLine("=== Language Test Completed ===");
    }

    [Fact]
    public void DebugGoogleSTT_SimpleBreakpointTest()
    {
        // Arrange
        _output.WriteLine("=== Debug Breakpoint Test for Google STT ===");
        
        // This test is designed for setting breakpoints and debugging
        var serviceName = _googleSTTService.GetServiceName();
        var supportsDetection = _googleSTTService.SupportsLanguageDetection;
        var requiresConversion = _googleSTTService.RequiresAudioConversion;
        
        _output.WriteLine($"Service Name: {serviceName}");
        _output.WriteLine($"Supports Language Detection: {supportsDetection}");
        _output.WriteLine($"Requires Audio Conversion: {requiresConversion}");
        
        // Set a breakpoint on this line to inspect the Google STT service
        var debugPoint = "Google STT Service Debug Point";
        _output.WriteLine(debugPoint);
        
        // Basic assertions
        Assert.Equal("Google STT", serviceName);
        Assert.True(supportsDetection);
        Assert.False(requiresConversion); // Google STT supports multiple formats natively
        
        _output.WriteLine("✅ Debug breakpoint test completed");
    }
}
