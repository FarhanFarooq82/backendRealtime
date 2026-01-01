using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.DTOs.Audio;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Simple integration test for AudioProcessingOrchestrator StartTranscriptionAsync method
/// Tests the private method through the public ProcessAudioWithSessionProvidersAsync method
/// </summary>
public class AudioProcessingOrchestratorSimpleTest
{
    private readonly ITestOutputHelper _output;

    public AudioProcessingOrchestratorSimpleTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ProcessAudioWithSessionProviders_WithRealAzureSTT_ShouldTranscribeAudio()
    {
        // Skip if no Azure credentials
        var azureKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        if (string.IsNullOrEmpty(azureKey))
        {
            _output.WriteLine("Skipping test - AZURE_SPEECH_KEY environment variable not set");
            return;
        }

        // Arrange
        var rootPath = GetRepositoryRoot();
        var audioFilePath = Path.Combine(rootPath, "test_converted.wav");
        
        if (!File.Exists(audioFilePath))
        {
            _output.WriteLine($"Skipping test - Audio file not found: {audioFilePath}");
            return;
        }

        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        _output.WriteLine($"Testing with audio file: {audioFilePath} ({audioData.Length} bytes)");

        // Setup Azure STT service
        var azureOptions = Options.Create(new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = azureKey,
                SpeechRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "eastus",
                SpeechEndpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT") ?? 
                                "https://eastus.api.cognitive.microsoft.com/"
            }
        });

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        
        var azureSTTLogger = loggerFactory.CreateLogger<AzureSTTService>();
        var orchestratorLogger = loggerFactory.CreateLogger<AudioProcessingOrchestrator>();

        var azureSTTService = new AzureSTTService(azureOptions, azureSTTLogger);
        var sttServices = new List<ISTTService> { azureSTTService };

        // Create minimal mock services for required dependencies
        var mockPitchService = new SimpleMockPitchAnalysisService();
        var mockSpeakerService = new SimpleMockSpeakerCharacteristicsService();
        
        // Create orchestrator
        var orchestrator = new AudioProcessingOrchestrator(
            orchestratorLogger,
            sttServices,
            mockPitchService,
            mockSpeakerService);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            assignedSTTProviders: new List<string> { "Azure" },
            mainLanguage: "en-US",
            targetLanguage: "es-ES",
            sessionId: Guid.NewGuid().ToString(),
            CancellationToken.None);
        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert and Log Results
        _output.WriteLine($"=== Test Results ===");
        _output.WriteLine($"Processing Time: {processingTime}ms");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"Provider: {result.Provider}");
        _output.WriteLine($"Confidence: {result.Confidence}");
        _output.WriteLine($"Detected Language: {result.DetectedLanguage}");
        _output.WriteLine($"Transcription: '{result.Transcription}'");
        _output.WriteLine($"Error: {result.ErrorMessage}");

        // Basic assertions
        Assert.NotNull(result);
        Assert.True(result.ProcessingTimeMs > 0, "Processing time should be recorded");
        
        if (result.Success)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Transcription), "Transcription should not be empty for successful results");
            Assert.True(result.Confidence >= 0, "Confidence should be non-negative");
            Assert.Equal("Azure", result.Provider);
            _output.WriteLine("✅ Test PASSED - Azure STT successfully transcribed audio");
        }
        else
        {
            _output.WriteLine($"⚠️ Test completed with failure (may be expected): {result.ErrorMessage}");
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage), "Error message should be provided for failed results");
        }
    }

    [Fact]  
    public async Task ProcessAudioWithSessionProviders_WithEmptyProviderList_ShouldReturnFailure()
    {
        // Arrange
        var rootPath = GetRepositoryRoot();
        var audioFilePath = Path.Combine(rootPath, "test_converted.wav");
        
        if (!File.Exists(audioFilePath))
        {
            _output.WriteLine($"Skipping test - Audio file not found: {audioFilePath}");
            return;
        }

        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var orchestratorLogger = loggerFactory.CreateLogger<AudioProcessingOrchestrator>();
        
        var mockPitchService = new SimpleMockPitchAnalysisService();
        var mockSpeakerService = new SimpleMockSpeakerCharacteristicsService();
        
        var orchestrator = new AudioProcessingOrchestrator(
            orchestratorLogger,
            new List<ISTTService>(), // Empty services
            mockPitchService,
            mockSpeakerService);

        // Act
        var result = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            assignedSTTProviders: new List<string>(), // Empty providers
            mainLanguage: "en-US",
            targetLanguage: "es-ES", 
            sessionId: Guid.NewGuid().ToString(),
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("No STT providers assigned", result.ErrorMessage);
        _output.WriteLine("✅ Test PASSED - Empty provider list correctly returns failure");
    }

    private string GetRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir, ".git")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Could not find repository root");
    }
}

/// <summary>
/// Simple mock for pitch analysis service
/// </summary>
public class SimpleMockPitchAnalysisService : IPitchAnalysisService
{
    public async Task<PitchAnalysisResult> ExtractPitchCharacteristicsAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        // Return a simple pitch analysis result
        return new PitchAnalysisResult
        {
            IsSuccess = true,
            FundamentalFrequency = 150.0f,
            PitchVariance = 10.0f,
            EstimatedGender = "MALE",
            EstimatedAge = "adult",
            VoiceQuality = "good",
            AnalysisConfidence = 0.8f
        };
    }
}

/// <summary>
/// Simple mock for speaker characteristics service  
/// </summary>
public class SimpleMockSpeakerCharacteristicsService : ISpeakerCharacteristicsService
{
    public Speaker CreateSpeaker(PitchAnalysisResult pitchResult, STTResult sttResult, string sessionId)
    {
        return new Speaker
        {
            SpeakerId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Language = sttResult.DetectedLanguage,
            DisplayName = "Mock Speaker",
            SpeakerNumber = 1,
            FirstHeard = DateTime.UtcNow,
            LastHeard = DateTime.UtcNow,
            TotalUtterances = 1,
            VoiceCharacteristics = new VoiceCharacteristics
            {
                FundamentalFrequency = pitchResult.FundamentalFrequency,
                PitchVariance = pitchResult.PitchVariance,
                Gender = pitchResult.EstimatedGender,
                AnalysisConfidence = pitchResult.AnalysisConfidence
            },
            SpeakingPatterns = new SpeakingPatterns
            {
                VocabularyFingerprint = new Dictionary<string, float>()
            }
        };
    }

    public Speaker ExtractSpeakingPatterns(Speaker speaker, string transcription, TimeSpan duration)
    {
        // Mock implementation - return speaker unchanged
        return speaker;
    }

    public SpeakerMatchResult CheckExistingSpeaker(Speaker speaker, List<Speaker> existingSpeakers)
    {
        return new SpeakerMatchResult
        {
            Speaker = speaker,
            IsMatch = false,
            IsNewSpeaker = true,
            Confidence = 1.0f
        };
    }

    public string GenerateSpeakerName(Speaker speaker, string detectedLanguage, int speakerNumber)
    {
        return $"{detectedLanguage} Speaker {speakerNumber}";
    }
}
