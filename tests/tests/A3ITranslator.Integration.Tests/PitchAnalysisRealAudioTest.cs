using Xunit;
using Xunit.Abstractions;
using A3ITranslator.Infrastructure.Services.Audio;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

namespace A3ITranslator.Integration.Tests
{
    public class PitchAnalysisRealAudioTest
    {
        private readonly ITestOutputHelper _output;
        
        public PitchAnalysisRealAudioTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ExtractPitchCharacteristics_WithRealAudio_ShouldGiveMeaningfulResults()
        {
            // Arrange
            var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var logger = loggerFactory.CreateLogger<PitchAnalysisService>();
            var service = new PitchAnalysisService(logger);
            
            // Test with real audio files
            var audioFiles = new[]
            {
                "/Users/farhanfarooq/Documents/GitHub/A3ITranslator/test_converted.wav",
                "/Users/farhanfarooq/Documents/GitHub/A3ITranslator/DEBUG_AUDIO_20251021_222926_dac6188d_audio.wav",
                "/Users/farhanfarooq/Documents/GitHub/A3ITranslator/test_simple_tone.wav"
            };

            foreach (var audioFile in audioFiles)
            {
                if (File.Exists(audioFile))
                {
                    _output.WriteLine($"\n=== Testing {Path.GetFileName(audioFile)} ===");
                    var audioData = await File.ReadAllBytesAsync(audioFile);
                    _output.WriteLine($"File size: {audioData.Length:N0} bytes ({audioData.Length / 1024.0:F1} KB)");
                    
                    // Act
                    var result = await service.ExtractPitchCharacteristicsAsync(audioData);
                    
                    // Assert & Output
                    _output.WriteLine($"Result: Success={result.IsSuccess}");
                    _output.WriteLine($"  F0: {result.FundamentalFrequency:F1} Hz");
                    _output.WriteLine($"  Gender: {result.EstimatedGender}");
                    _output.WriteLine($"  Age: {result.EstimatedAge}");
                    _output.WriteLine($"  Quality: {result.VoiceQuality}");
                    _output.WriteLine($"  Confidence: {result.AnalysisConfidence:F2}");
                    _output.WriteLine($"  Variance: {result.PitchVariance:F1}");
                    
                    // With real audio files, we should NOT get the default values
                    if (result.IsSuccess)
                    {
                        Assert.True(result.FundamentalFrequency != 150.0f || result.EstimatedGender != "UNKNOWN", 
                            "Real audio should not return default pitch values");
                    }
                }
                else
                {
                    _output.WriteLine($"File not found: {audioFile}");
                }
            }
        }

        [Fact]
        public async Task ExtractPitchCharacteristics_WithSmallTestData_ShouldReturnDefaults()
        {
            // Arrange
            var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var logger = loggerFactory.CreateLogger<PitchAnalysisService>();
            var service = new PitchAnalysisService(logger);
            
            // Small synthetic data should return defaults
            var testData = new byte[100];  
            for(int i = 0; i < testData.Length; i++) {
                testData[i] = (byte)(i % 256);
            }
            
            _output.WriteLine($"Testing with {testData.Length} bytes of synthetic data");
            
            // Act
            var result = await service.ExtractPitchCharacteristicsAsync(testData);
            
            // Assert
            _output.WriteLine($"Result: Success={result.IsSuccess}");
            _output.WriteLine($"  F0: {result.FundamentalFrequency:F1} Hz");
            _output.WriteLine($"  Gender: {result.EstimatedGender}");
            _output.WriteLine($"  Confidence: {result.AnalysisConfidence:F2}");
            
            // Small test data should return defaults
            Assert.False(result.IsSuccess);
            Assert.Equal(150.0f, result.FundamentalFrequency);
            Assert.Equal("UNKNOWN", result.EstimatedGender);
        }
    }
}
