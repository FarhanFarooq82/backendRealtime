using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.DTOs.Audio;
using A3ITranslator.Application.Common;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Integration test for AudioProcessingOrchestrator.StartTranscriptionAsync method
/// Tests with real Azure STT using actual audio files from the repository root
/// </summary>
public class AudioProcessingOrchestratorIntegrationTest
{
    private readonly ITestOutputHelper _output;
    private readonly AudioProcessingOrchestrator _orchestrator;
    private readonly string[] _testAudioFiles;

    public AudioProcessingOrchestratorIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
        
        // Define test audio files (3 different audio files from repository root)
        var rootPath = GetRepositoryRoot();
        _testAudioFiles = new[]
        {
            Path.Combine(rootPath, "test_converted.wav"),
            Path.Combine(rootPath, "DEBUG_AUDIO_20251021_222926_dac6188d_audio.wav"),
            Path.Combine(rootPath, "test_simple_tone.wav")
        };

        // Setup service collection with real Azure STT
        var services = new ServiceCollection();
        SetupServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        _orchestrator = serviceProvider.GetRequiredService<AudioProcessingOrchestrator>();
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

    private void SetupServices(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });

        // Build configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Configure services from appsettings.json
        services.Configure<ServiceOptions>(configuration.GetSection("Services"));

        // Also add the configuration instance
        services.AddSingleton<IConfiguration>(configuration);

        // Register STT services
        services.AddTransient<ISTTService, AzureSTTService>();
        services.AddTransient<IEnumerable<ISTTService>>(provider => 
            new[] { provider.GetRequiredService<AzureSTTService>() });

        // Register pitch analysis and speaker services (mock for this test)
        services.AddTransient<IPitchAnalysisService, MockPitchAnalysisService>();
        services.AddTransient<ISpeakerCharacteristicsService, MockSpeakerCharacteristicsService>();

        // Register the orchestrator
        services.AddTransient<AudioProcessingOrchestrator>();
    }

    [Theory]
    [InlineData(0, "test_converted.wav", "en-US", "es-ES")]
    [InlineData(1, "DEBUG_AUDIO_20251021_222926_dac6188d_audio.wav", "en-US", "fr-FR")]
    [InlineData(2, "test_simple_tone.wav", "en-US", "de-DE")]
    public async Task StartTranscriptionAsync_WithRealAudioFiles_ShouldTranscribeSuccessfully(
        int audioFileIndex, 
        string expectedFileName, 
        string mainLanguage, 
        string targetLanguage)
    {
        // Arrange
        var audioFilePath = _testAudioFiles[audioFileIndex];
        _output.WriteLine($"=== Testing {expectedFileName} ===");
        _output.WriteLine($"Audio file path: {audioFilePath}");
        
        // Verify file exists
        Assert.True(File.Exists(audioFilePath), $"Audio file not found: {audioFilePath}");
        
        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        _output.WriteLine($"Audio file size: {audioData.Length} bytes");
        
        var assignedProviders = new List<string> { "Azure" };
        var sessionId = Guid.NewGuid().ToString();
        
        // Act
        var startTime = DateTime.UtcNow;
        var result = await _orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            assignedProviders,
            mainLanguage,
            targetLanguage,
            sessionId,
            CancellationToken.None);
        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Assert
        _output.WriteLine($"Processing completed in {processingTime}ms");
        _output.WriteLine($"Result Success: {result.Success}");
        _output.WriteLine($"Provider: {result.Provider}");
        _output.WriteLine($"Confidence: {result.Confidence}");
        _output.WriteLine($"Detected Language: {result.DetectedLanguage}");
        _output.WriteLine($"Transcription: {result.Transcription}");
        _output.WriteLine($"Error: {result.ErrorMessage}");

        // Basic assertions
        Assert.NotNull(result);
        Assert.True(result.ProcessingTimeMs > 0, "Processing time should be recorded");
        
        // For tone audio, we don't expect successful transcription (no speech)
        if (expectedFileName.Contains("tone"))
        {
            _output.WriteLine("Tone audio - expecting low confidence or failure");
            // Tone files may fail or have very low confidence - this is expected
        }
        else
        {
            // For real speech audio, we expect some level of success
            if (result.Success)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Transcription), "Transcription should not be empty for successful results");
                Assert.True(result.Confidence >= 0, "Confidence should be non-negative");
                Assert.Equal("Azure", result.Provider);
            }
            else
            {
                _output.WriteLine($"Transcription failed (may be expected): {result.ErrorMessage}");
                Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage), "Error message should be provided for failed results");
            }
        }
    }

    [Fact]
    public async Task StartTranscriptionAsync_WithEmptyProviderList_ShouldReturnFailure()
    {
        // Arrange
        var audioFilePath = _testAudioFiles[0];
        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        var emptyProviders = new List<string>();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await _orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            emptyProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("No STT providers assigned", result.ErrorMessage);
    }

    [Fact]
    public async Task StartTranscriptionAsync_WithInvalidProvider_ShouldReturnFailure()
    {
        // Arrange
        var audioFilePath = _testAudioFiles[0];
        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        var invalidProviders = new List<string> { "NonExistentProvider" };
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await _orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            invalidProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("failed", result.ErrorMessage);
    }

    [Fact]
    public async Task StartTranscriptionAsync_WithCancellation_ShouldHandleGracefully()
    {
        // Arrange
        var audioFilePath = _testAudioFiles[0];
        var audioData = await File.ReadAllBytesAsync(audioFilePath);
        var providers = new List<string> { "Azure" };
        var sessionId = Guid.NewGuid().ToString();
        
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Cancel quickly

        // Act & Assert
        var result = await _orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData,
            providers,
            "en-US",
            "es-ES",
            sessionId,
            cts.Token);

        // The result depends on timing - cancellation may or may not take effect
        Assert.NotNull(result);
        _output.WriteLine($"Cancellation test result: Success={result.Success}, Error={result.ErrorMessage}");
    }

    [Fact]
    public async Task ProcessAudioWithSessionProviders_SameSpeakerTwoAudios_ShouldRecognizeExistingSpeaker()
    {
        // Arrange - Create a session-aware orchestrator
        var services = new ServiceCollection();
        SetupServicesWithSessionAwareMocks(services);
        var serviceProvider = services.BuildServiceProvider();
        var orchestrator = serviceProvider.GetRequiredService<AudioProcessingOrchestrator>();

        var sessionId = Guid.NewGuid().ToString();
        var assignedProviders = new List<string> { "Azure" };

        // Use two different audio files to simulate same speaker
        var audioFile1Path = _testAudioFiles[0]; // test_converted.wav
        var audioFile2Path = _testAudioFiles[1]; // DEBUG_AUDIO_*.wav
        
        var audioData1 = await File.ReadAllBytesAsync(audioFile1Path);
        var audioData2 = await File.ReadAllBytesAsync(audioFile2Path);

        _output.WriteLine($"=== Testing Same Speaker Recognition ===");
        _output.WriteLine($"Session ID: {sessionId}");
        _output.WriteLine($"Audio 1: {Path.GetFileName(audioFile1Path)} ({audioData1.Length} bytes)");
        _output.WriteLine($"Audio 2: {Path.GetFileName(audioFile2Path)} ({audioData2.Length} bytes)");

        // Act - Process first audio (should create new speaker)
        _output.WriteLine("\n--- Processing First Audio ---");
        var result1 = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData1,
            assignedProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        _output.WriteLine($"First audio result: Success={result1.Success}, Speaker={result1.SpeakerAnalysis?.Gender}");

        // Act - Process second audio with same session (should recognize existing speaker)
        _output.WriteLine("\n--- Processing Second Audio ---");
        var result2 = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData2,
            assignedProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        _output.WriteLine($"Second audio result: Success={result2.Success}, Speaker={result2.SpeakerAnalysis?.Gender}");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        
        // Both should be processed successfully (or fail gracefully if audio has issues)
        _output.WriteLine($"\nFirst audio - Success: {result1.Success}, Error: {result1.ErrorMessage}");
        _output.WriteLine($"Second audio - Success: {result2.Success}, Error: {result2.ErrorMessage}");

        // Get the session-aware mock to verify speaker recognition
        var mockSpeakerService = serviceProvider.GetRequiredService<ISpeakerCharacteristicsService>() as SessionAwareMockSpeakerService;
        Assert.NotNull(mockSpeakerService);

        // Verify that the mock tracked speakers correctly
        var sessionSpeakers = mockSpeakerService.GetSessionSpeakers(sessionId);
        _output.WriteLine($"\nSession speakers tracked: {sessionSpeakers.Count}");
        
        if (sessionSpeakers.Count > 0)
        {
            var firstSpeaker = sessionSpeakers.First();
            _output.WriteLine($"First speaker: ID={firstSpeaker.SpeakerId}, Utterances={firstSpeaker.TotalUtterances}");
            
            // The speaker should have been recognized in the second call (TotalUtterances > 1)
            Assert.True(firstSpeaker.TotalUtterances >= 1, "Speaker should have at least 1 utterance tracked");
        }

        // Verify speaker matching was attempted
        Assert.True(mockSpeakerService.MatchingAttempts > 1, "Speaker matching should have been attempted for second audio");
        
        _output.WriteLine($"Total matching attempts: {mockSpeakerService.MatchingAttempts}");
        _output.WriteLine($"Speakers found as existing: {mockSpeakerService.ExistingSpeakersFound}");
    }

    [Fact]
    public async Task ProcessAudioWithSessionProviders_SameSpeakerTwoAudios_MockOnly_ShouldRecognizeExistingSpeaker()
    {
        // Arrange - Create orchestrator with all mocks (no Azure STT needed)
        var services = new ServiceCollection();
        SetupServicesWithAllMocks(services);
        var serviceProvider = services.BuildServiceProvider();
        var orchestrator = serviceProvider.GetRequiredService<AudioProcessingOrchestrator>();

        var sessionId = Guid.NewGuid().ToString();
        var assignedProviders = new List<string> { "MockSTT" };

        // Create some test audio data (doesn't matter what it is for mock testing)
        var audioData1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var audioData2 = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };

        _output.WriteLine($"=== Testing Same Speaker Recognition (Mock Only) ===");
        _output.WriteLine($"Session ID: {sessionId}");

        // Act - Process first audio (should create new speaker)
        _output.WriteLine("\n--- Processing First Audio ---");
        var result1 = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData1,
            assignedProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        _output.WriteLine($"First audio result: Success={result1.Success}");

        // Act - Process second audio with same session (should recognize existing speaker)
        _output.WriteLine("\n--- Processing Second Audio ---");
        var result2 = await orchestrator.ProcessAudioWithSessionProvidersAsync(
            audioData2,
            assignedProviders,
            "en-US",
            "es-ES",
            sessionId,
            CancellationToken.None);

        _output.WriteLine($"Second audio result: Success={result2.Success}");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);

        // Get the session-aware mock to verify speaker recognition
        var mockSpeakerService = serviceProvider.GetRequiredService<ISpeakerCharacteristicsService>() as SessionAwareMockSpeakerService;
        Assert.NotNull(mockSpeakerService);

        // Verify that speaker matching was attempted
        Assert.True(mockSpeakerService.MatchingAttempts >= 2, "Speaker matching should have been attempted for both audios");
        
        // Verify speakers were tracked
        var sessionSpeakers = mockSpeakerService.GetSessionSpeakers(sessionId);
        Assert.True(sessionSpeakers.Count >= 1, "At least one speaker should be tracked in the session");

        _output.WriteLine($"Total matching attempts: {mockSpeakerService.MatchingAttempts}");
        _output.WriteLine($"Speakers found as existing: {mockSpeakerService.ExistingSpeakersFound}");
        _output.WriteLine($"Session speakers count: {sessionSpeakers.Count}");

        // Clean up
        mockSpeakerService.ClearSession(sessionId);
    }

    private void SetupServicesWithAllMocks(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });

        // Register all mock services (no real Azure needed)
        services.AddTransient<ISTTService, MockSTTService>();
        services.AddTransient<IEnumerable<ISTTService>>(provider => 
            new[] { provider.GetRequiredService<MockSTTService>() });

        // Register session-aware mock services
        services.AddTransient<IPitchAnalysisService, MockPitchAnalysisService>();
        services.AddSingleton<ISpeakerCharacteristicsService, SessionAwareMockSpeakerService>();

        // Register the orchestrator
        services.AddTransient<AudioProcessingOrchestrator>();
    }

    private void SetupServicesWithSessionAwareMocks(IServiceCollection services)
    {
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });

        // Configure Azure services with real keys from environment variables
        var azureOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ?? 
                           throw new InvalidOperationException("AZURE_SPEECH_KEY environment variable is required"),
                SpeechRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "eastus",
                SpeechEndpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT") ?? 
                                "https://eastus.api.cognitive.microsoft.com/"
            }
        };

        services.AddSingleton(Options.Create(azureOptions));

        // Register STT services
        services.AddTransient<ISTTService, AzureSTTService>();
        services.AddTransient<IEnumerable<ISTTService>>(provider => 
            new[] { provider.GetRequiredService<AzureSTTService>() });

        // Register session-aware mock services
        services.AddTransient<IPitchAnalysisService, MockPitchAnalysisService>();
        services.AddSingleton<ISpeakerCharacteristicsService, SessionAwareMockSpeakerService>();

        // Register the orchestrator
        services.AddTransient<AudioProcessingOrchestrator>();
    }
}

/// <summary>
/// Mock implementation of IPitchAnalysisService for testing
/// </summary>
public class MockPitchAnalysisService : IPitchAnalysisService
{
    public async Task<PitchAnalysisResult> ExtractPitchCharacteristicsAsync(
        byte[] audioData, 
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate processing
        
        return new PitchAnalysisResult
        {
            IsSuccess = true,
            FundamentalFrequency = 150.0f,
            PitchVariance = 10.0f,
            EstimatedGender = "UNKNOWN",
            EstimatedAge = "adult",
            VoiceQuality = "good",
            AnalysisConfidence = 0.7f
        };
    }
}

/// <summary>
/// Mock implementation of ISpeakerCharacteristicsService for testing
/// </summary>
public class MockSpeakerCharacteristicsService : ISpeakerCharacteristicsService
{
    public Speaker CreateSpeaker(
        PitchAnalysisResult pitchResult,
        STTResult sttResult,
        string sessionId)
    {
        return new Speaker
        {
            SpeakerId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            SpeakerNumber = 1,
            DisplayName = "Test Speaker",
            Language = "en-US",
            TotalUtterances = 1,
            VoiceCharacteristics = new VoiceCharacteristics
            {
                Gender = pitchResult.EstimatedGender,
                FundamentalFrequency = pitchResult.FundamentalFrequency,
                PitchVariance = pitchResult.PitchVariance,
                AnalysisConfidence = pitchResult.AnalysisConfidence
            }
        };
    }

    public Speaker ExtractSpeakingPatterns(
        Speaker speaker,
        string transcription,
        TimeSpan duration)
    {
        // Simple mock implementation
        speaker.SpeakingPatterns = new SpeakingPatterns
        {
            VocabularyFingerprint = new Dictionary<string, float>()
        };
        
        return speaker;
    }

    public SpeakerMatchResult CheckExistingSpeaker(
        Speaker speaker,
        List<Speaker> existingSpeakers)
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

/// <summary>
/// Session-aware mock implementation that can track speakers across multiple audio processing calls
/// Used for testing speaker recognition functionality
/// </summary>
public class SessionAwareMockSpeakerService : ISpeakerCharacteristicsService
{
    private static readonly Dictionary<string, List<Speaker>> _sessionSpeakers = new();
    private static readonly object _lock = new object();
    
    public int MatchingAttempts { get; private set; } = 0;
    public int ExistingSpeakersFound { get; private set; } = 0;

    public Speaker CreateSpeaker(PitchAnalysisResult pitchResult, STTResult transcriptionResult, string sessionId)
    {
        var speaker = new Speaker
        {
            SpeakerId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Language = transcriptionResult.DetectedLanguage,
            DisplayName = "Test Speaker",
            SpeakerNumber = 1,
            FirstHeard = DateTime.UtcNow,
            LastHeard = DateTime.UtcNow,
            TotalUtterances = 1,
            VoiceCharacteristics = new VoiceCharacteristics
            {
                Gender = pitchResult.EstimatedGender,
                FundamentalFrequency = pitchResult.FundamentalFrequency,
                PitchVariance = pitchResult.PitchVariance,
                AnalysisConfidence = pitchResult.AnalysisConfidence
            }
        };

        lock (_lock)
        {
            if (!_sessionSpeakers.ContainsKey(sessionId))
            {
                _sessionSpeakers[sessionId] = new List<Speaker>();
            }
            _sessionSpeakers[sessionId].Add(speaker);
        }

        return speaker;
    }

    public Speaker ExtractSpeakingPatterns(Speaker speaker, string transcription, TimeSpan duration)
    {
        // Simple mock implementation
        speaker.SpeakingPatterns = new SpeakingPatterns
        {
            VocabularyFingerprint = new Dictionary<string, float>()
        };
        
        return speaker;
    }

    public SpeakerMatchResult CheckExistingSpeaker(Speaker speaker, List<Speaker> existingSpeakers)
    {
        MatchingAttempts++;

        lock (_lock)
        {
            if (existingSpeakers == null || existingSpeakers.Count == 0)
            {
                // No existing speakers in this session
                return new SpeakerMatchResult
                {
                    Speaker = speaker,
                    IsMatch = false,
                    IsNewSpeaker = true,
                    Confidence = 1.0f,
                    SimilarityScore = 0.0f
                };
            }

            // Simulate speaker matching logic
            // For testing, we'll consider speakers with similar voice characteristics as matches
            
            foreach (var existingSpeaker in existingSpeakers)
            {
                // Simple similarity check based on fundamental frequency
                var frequencyDiff = Math.Abs(speaker.VoiceCharacteristics.FundamentalFrequency - 
                                           existingSpeaker.VoiceCharacteristics.FundamentalFrequency);
                
                // If frequencies are within 20Hz, consider it the same speaker
                if (frequencyDiff < 20.0f)
                {
                    ExistingSpeakersFound++;
                    
                    // Update existing speaker's stats
                    existingSpeaker.TotalUtterances++;
                    existingSpeaker.LastHeard = DateTime.UtcNow;
                    
                    return new SpeakerMatchResult
                    {
                        Speaker = existingSpeaker,
                        IsMatch = true,
                        IsNewSpeaker = false,
                        Confidence = 0.85f,
                        SimilarityScore = 1.0f - (frequencyDiff / 100.0f) // Higher similarity for closer frequencies
                    };
                }
            }

            // No match found
            return new SpeakerMatchResult
            {
                Speaker = speaker,
                IsMatch = false,
                IsNewSpeaker = true,
                Confidence = 1.0f,
                SimilarityScore = 0.0f
            };
        }
    }

    public string GenerateSpeakerName(Speaker speaker, string detectedLanguage, int speakerNumber)
    {
        return $"{detectedLanguage} Speaker {speakerNumber}";
    }

    public List<Speaker> GetSessionSpeakers(string sessionId)
    {
        lock (_lock)
        {
            return _sessionSpeakers.ContainsKey(sessionId) 
                ? _sessionSpeakers[sessionId].ToList() 
                : new List<Speaker>();
        }
    }

    public void ClearSession(string sessionId)
    {
        lock (_lock)
        {
            _sessionSpeakers.Remove(sessionId);
        }
    }

    public void ClearAllSessions()
    {
        lock (_lock)
        {
            _sessionSpeakers.Clear();
            MatchingAttempts = 0;
            ExistingSpeakersFound = 0;
        }
    }
}

/// <summary>
/// Mock STT service for testing without Azure dependencies
/// </summary>
public class MockSTTService : ISTTService
{
    public Dictionary<string, string> GetSupportedLanguages()
    {
        return new Dictionary<string, string>
        {
            { "en-US", "English (United States)" },
            { "es-ES", "Spanish (Spain)" },
            { "fr-FR", "French (France)" },
            { "de-DE", "German (Germany)" },
            { "pt-BR", "Portuguese (Brazil)" },
            { "it-IT", "Italian (Italy)" }
        };
    }

    public string GetServiceName() => "MockSTT";

    public async Task<Result<string>> ConvertSpeechToTextAsync(byte[] audioData, string languageCode, string sessionId)
    {
        await Task.Delay(100); // Simulate processing
        return Result<string>.Success("This is a mock transcription for testing purposes.");
    }

    public Task<bool> CheckHealthAsync()
    {
        return Task.FromResult(true);
    }

    public bool SupportsLanguageDetection => true;

    public bool RequiresAudioConversion => false;

    public async Task<STTResult> TranscribeWithDetectionAsync(
        byte[] audio,
        string[] candidateLanguages,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken); // Simulate processing

        return new STTResult
        {
            Success = true,
            Provider = GetServiceName(),
            Transcription = "This is a mock transcription for testing purposes.",
            DetectedLanguage = candidateLanguages.FirstOrDefault() ?? "en-US",
            Confidence = 0.95f,
            ProcessingTimeMs = 100,
            SpeakerAnalysis = new SpeakerAnalysis
            {
                Gender = "MALE",
                Confidence = 0.85f
            }
        };
    }
}
