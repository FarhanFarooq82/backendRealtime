# AudioProcessingOrchestrator Integration Test

This directory contains integration tests for the `AudioProcessingOrchestrator.StartTranscriptionAsync` method using real Azure STT services and actual audio files.

## Test Files Created

1. **AudioProcessingOrchestratorSimpleTest.cs** - Simple integration test focusing on the core transcription functionality
2. **appsettings.json** - Configuration template for Azure services
3. **Audio Files Used:**
   - `test_converted.wav` (existing)
   - `DEBUG_AUDIO_20251021_222926_dac6188d_audio.wav` (existing)
   - `test_simple_tone.wav` (created - 3 second tone at 440Hz)

## Prerequisites

### 1. Azure Speech Service Setup
You need to set up environment variables with your Azure Speech Service credentials:

```bash
export AZURE_SPEECH_KEY="your_azure_speech_key_here"
export AZURE_SPEECH_REGION="eastus"  # or your preferred region
export AZURE_SPEECH_ENDPOINT="https://eastus.api.cognitive.microsoft.com/"
```

### 2. Audio Files
The test looks for audio files in the repository root:
- Ensure `test_converted.wav` exists in `/Users/farhanfarooq/Documents/GitHub/A3ITranslator/`
- The test will automatically skip if audio files are not found

## Running the Tests

### Option 1: Run Specific Test
```bash
cd /Users/farhanfarooq/Documents/GitHub/A3ITranslator/backendCSharp/tests/A3ITranslator.Integration.Tests
dotnet test --filter "ProcessAudioWithSessionProviders_WithRealAzureSTT_ShouldTranscribeAudio" --logger "console;verbosity=detailed"
```

### Option 2: Run All AudioProcessing Tests
```bash
dotnet test --filter "AudioProcessingOrchestrator" --logger "console;verbosity=detailed"
```

### Option 3: Manual Test Execution
If the build has issues, you can create a simple console app to test the method:

```csharp
// Create a simple console app in the test project
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

var azureKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
if (string.IsNullOrEmpty(azureKey))
{
    Console.WriteLine("AZURE_SPEECH_KEY not set");
    return;
}

var audioData = File.ReadAllBytes("/Users/farhanfarooq/Documents/GitHub/A3ITranslator/test_converted.wav");

var options = Options.Create(new ServiceOptions
{
    Azure = new AzureOptions
    {
        SpeechKey = azureKey,
        SpeechRegion = "eastus",
        SpeechEndpoint = "https://eastus.api.cognitive.microsoft.com/"
    }
});

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var azureLogger = loggerFactory.CreateLogger<AzureSTTService>();
var orchestratorLogger = loggerFactory.CreateLogger<AudioProcessingOrchestrator>();

var azureSTT = new AzureSTTService(options, azureLogger);
var services = new List<ISTTService> { azureSTT };

// Note: You'll need to provide mock implementations for IPitchAnalysisService and ISpeakerCharacteristicsService
// var orchestrator = new AudioProcessingOrchestrator(orchestratorLogger, services, mockPitch, mockSpeaker);

// var result = await orchestrator.ProcessAudioWithSessionProvidersAsync(
//     audioData, 
//     new List<string> { "Azure" }, 
//     "en-US", 
//     "es-ES", 
//     Guid.NewGuid().ToString());

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Transcription: {result.Transcription}");
```

## Expected Test Results

### Successful Test Output:
```
=== Test Results ===
Processing Time: 2543ms
Success: True
Provider: Azure
Confidence: 0.85
Detected Language: en-US
Transcription: 'Hello, this is a test audio file.'
Error: 
✅ Test PASSED - Azure STT successfully transcribed audio
```

### Failed Test (No Audio):
```
Skipping test - Audio file not found: /path/to/audio.wav
```

### Failed Test (No Credentials):
```
Skipping test - AZURE_SPEECH_KEY environment variable not set
```

## Key Test Components

### What the Test Validates:
1. **Real Azure STT Integration** - Uses actual Azure Speech-to-Text service
2. **StartTranscriptionAsync Method** - Tests the private method through the public wrapper
3. **Audio File Processing** - Processes real WAV files from the repository
4. **Error Handling** - Tests empty provider lists and missing services
5. **Performance Metrics** - Measures and reports processing time

### Test Method Focus:
The test specifically validates the `StartTranscriptionAsync` method inside `AudioProcessingOrchestrator` by:
- Calling `ProcessAudioWithSessionProvidersAsync` (public method)
- Which internally calls `StartTranscriptionAsync` (private method)
- Using real Azure STT service with actual audio files
- Testing with different language pairs (en-US → es-ES, en-US → fr-FR, etc.)

## Troubleshooting

1. **Build Errors**: The main application may have some build issues. Focus on running just the test project.
2. **Missing Interfaces**: Some mock implementations may need to be adjusted based on the actual interface definitions.
3. **Audio Format Issues**: Ensure audio files are in WAV format, 16kHz, mono channel.
4. **Azure Quota**: Check your Azure Speech Service quota if tests fail with service errors.

## Next Steps

Once the basic test is working, you can:
1. Add more audio files for comprehensive testing
2. Test different language combinations
3. Add performance benchmarking
4. Test error scenarios (corrupted audio, network issues)
5. Validate speaker identification features (when pitch analysis is working)
