using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Debug test for Azure STT Service with actual audio files
/// This test requires valid Azure Speech credentials in appsettings.Development.json
/// </summary>
public class AzureSTTServiceDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<AzureSTTService> _logger;

    public AzureSTTServiceDebugTest(ITestOutputHelper output)
    {
        _output = output;
        
        // Create test logger
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<AzureSTTService>();
    }

    [Fact]
    public async Task TestAzureSTTWithDebugAudioFile()
    {
        // Skip test if no debug audio files found
        var rootPath = "/Users/farhanfarooq/Documents/GitHub/A3ITranslator";
        var debugAudioFiles = Directory.GetFiles(rootPath, "DEBUG_AUDIO_*.webm")
            .Concat(Directory.GetFiles(rootPath, "DEBUG_AUDIO_*.wav"))
            .Concat(Directory.GetFiles(rootPath, "DEBUG_AUDIO_*.mp3"))
            .ToArray();

        if (!debugAudioFiles.Any())
        {
            _output.WriteLine("No debug audio files found. Upload an audio file through the API first to generate debug files.");
            return; // Skip test
        }

        // Use the most recent debug audio file
        var latestAudioFile = debugAudioFiles
            .OrderByDescending(f => File.GetCreationTime(f))
            .First();

        _output.WriteLine($"Testing with debug audio file: {latestAudioFile}");
        _output.WriteLine($"File size: {new FileInfo(latestAudioFile).Length} bytes");

        // Load audio file
        var audioData = await File.ReadAllBytesAsync(latestAudioFile);
        
        // Create Azure STT service configuration
        var serviceOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ?? "YOUR_AZURE_SPEECH_KEY_HERE",
                SpeechRegion = "northeurope", // TODO: Replace with actual region
                SpeechEndpoint = "https://northeurope.api.cognitive.microsoft.com/", // TODO: Replace with actual endpoint
            }
        };

        var options = Options.Create(serviceOptions);
        var azureSTTService = new AzureSTTService(options, _logger);

        // Test language detection with multiple candidate languages
        var candidateLanguages = new[] { "da-DK", "ur-IN"};

        _output.WriteLine($"Starting Azure STT transcription with candidate languages: [{string.Join(", ", candidateLanguages)}]");

        try
        {
            // Perform transcription
            var result = await azureSTTService.TranscribeWithDetectionAsync(
                audioData, 
                candidateLanguages, 
                CancellationToken.None);

            // Log results
            _output.WriteLine("=== Azure STT Results ===");
            _output.WriteLine($"Success: {result.Success}");
            _output.WriteLine($"Provider: {result.Provider}");
            _output.WriteLine($"Processing Time: {result.ProcessingTimeMs}ms");
            
            if (result.Success)
            {
                _output.WriteLine($"Transcription: {result.Transcription}");
                _output.WriteLine($"Detected Language: {result.DetectedLanguage}");
                _output.WriteLine($"Confidence: {result.Confidence:F3}");
                
                if (result.SpeakerAnalysis != null)
                {
                    _output.WriteLine($"Speaker: {result.SpeakerAnalysis.SpeakerLabel}");
                    _output.WriteLine($"Gender: {result.SpeakerAnalysis.Gender}");
                    _output.WriteLine($"Age Range: {result.SpeakerAnalysis.EstimatedAgeRange}");
                }

                if (result.Words?.Any() == true)
                {
                    _output.WriteLine($"Word Count: {result.Words.Count}");
                    _output.WriteLine("First 5 words with timing:");
                    foreach (var word in result.Words.Take(5))
                    {
                        _output.WriteLine($"  '{word.Word}': {word.StartTime:mm\\:ss\\.fff} - {word.EndTime:mm\\:ss\\.fff} (confidence: {word.Confidence:F3})");
                    }
                }

                // Assert successful transcription
                Assert.True(result.Success, "Transcription should be successful");
                Assert.False(string.IsNullOrWhiteSpace(result.Transcription), "Transcription should not be empty");
                Assert.True(result.Confidence > 0, "Confidence should be greater than 0");
                Assert.NotNull(result.DetectedLanguage);
            }
            else
            {
                _output.WriteLine($"Error: {result.ErrorMessage}");
                Assert.Fail($"Azure STT failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Exception during Azure STT test: {ex.Message}");
            _output.WriteLine($"Stack trace: {ex.StackTrace}");
            
            // Log configuration issues if it's a credentials problem
            if (ex.Message.Contains("credentials") || ex.Message.Contains("key") || ex.Message.Contains("unauthorized"))
            {
                _output.WriteLine("CONFIGURATION ISSUE: Make sure to update the Azure Speech credentials in this test:");
                _output.WriteLine("1. Replace 'YOUR_AZURE_SPEECH_KEY' with your actual Azure Speech key");
                _output.WriteLine("2. Update the region if different from 'eastus'");
                _output.WriteLine("3. Or configure credentials in appsettings.Development.json");
            }
            
            throw; // Re-throw to fail the test
        }
    }

    [Fact]
    public async Task TestAzureSTTServiceHealth()
    {
        // Create minimal configuration for health check
        var serviceOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = "test-key", // Dummy key for health check
                SpeechRegion = "eastus",
            }
        };

        var options = Options.Create(serviceOptions);
        var azureSTTService = new AzureSTTService(options, _logger);

        // Test health check
        var isHealthy = await azureSTTService.CheckHealthAsync();
        
        _output.WriteLine($"Azure STT Service Health: {(isHealthy ? "Healthy" : "Unhealthy")}");
        
        // Health check should pass with any configuration (it just checks if config exists)
        Assert.True(isHealthy, "Azure STT service health check should pass with configuration present");
    }

    [Fact]
    public void TestAzureSTTSupportedLanguages()
    {
        // Test that the service returns the expected supported languages
        var serviceOptions = new ServiceOptions { 
            Azure = new AzureOptions() {
                SpeechKey = "dummy",
                SpeechRegion = "eastus",
            }
        };
        var options = Options.Create(serviceOptions);
        var azureSTTService = new AzureSTTService(options, _logger);

        var supportedLanguages = azureSTTService.GetSupportedLanguages();
        
        _output.WriteLine($"Azure STT Supported Languages Count: {supportedLanguages.Count}");
        _output.WriteLine("Sample languages:");
        foreach (var lang in supportedLanguages.Take(10))
        {
            _output.WriteLine($"  {lang.Key}: {lang.Value}");
        }

        // Verify key languages are supported
        Assert.True(supportedLanguages.ContainsKey("en-US"), "English (US) should be supported");
        Assert.True(supportedLanguages.ContainsKey("ur-PK"), "Urdu should be supported");
        Assert.True(supportedLanguages.ContainsKey("ar-SA"), "Arabic should be supported");
        Assert.True(supportedLanguages.Count > 50, "Should support many languages");
    }
}
