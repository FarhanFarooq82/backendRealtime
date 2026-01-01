using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Test to verify OGG to WAV audio conversion is actually working
/// This test will save converted files so you can manually verify them
/// </summary>
public class AzureSTTAudioConversionTest
{
    private readonly ITestOutputHelper _output;
    private readonly AzureSTTService _azureSTTService;
    private readonly string _testOutputDir;

    public AzureSTTAudioConversionTest(ITestOutputHelper output)
    {
        _output = output;
        
        // Create test output directory
        _testOutputDir = Path.Combine(Environment.CurrentDirectory, "test_audio_output");
        Directory.CreateDirectory(_testOutputDir);
        
        // Configure Azure STT service with test settings
        var serviceOptions = new ServiceOptions
        {
            Azure = new AzureOptions
            {
                SpeechKey = "test-key-for-conversion-test",
                SpeechRegion = "northeurope",
                SpeechEndpoint = "https://northeurope.api.cognitive.microsoft.com/"
            }
        };
        
        var options = Options.Create(serviceOptions);
        var logger = new LoggerFactory().CreateLogger<AzureSTTService>();
        
        _azureSTTService = new AzureSTTService(options, logger);
    }

    [Fact]
    public async Task ConvertWebMToWav_ShouldCreateValidWavFile()
    {
        // Arrange - Look for .ogg files (which are actually WebM format)
        var rootWebMFiles = Directory.GetFiles("/Users/farhanfarooq/Documents/GitHub/A3ITranslator", "*.ogg");
        
        if (!rootWebMFiles.Any())
        {
            _output.WriteLine("❌ No WebM files found in root directory for testing");
            Assert.Fail("No WebM files (.ogg extension) found for testing. Please ensure WebM files exist in the root directory.");
            return;
        }

        _output.WriteLine($"🎵 Found {rootWebMFiles.Length} WebM files for testing:");
        foreach (var file in rootWebMFiles)
        {
            _output.WriteLine($"   - {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
        }

        // Test each WebM file
        for (int i = 0; i < rootWebMFiles.Length; i++)
        {
            var webmFile = rootWebMFiles[i];
            var webmFileName = Path.GetFileName(webmFile);
            
            _output.WriteLine($"\n🔄 Testing conversion of: {webmFileName}");

            // Read WebM file
            byte[] webmData = await File.ReadAllBytesAsync(webmFile);
            _output.WriteLine($"   📁 Original WebM size: {webmData.Length:N0} bytes");

            // Verify it's actually a WebM file (header: 1A-45-DF-A3)
            if (webmData.Length >= 4)
            {
                var webmHeader = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
                var actualHeader = webmData.Take(4).ToArray();
                var headerHex = BitConverter.ToString(actualHeader);
                
                _output.WriteLine($"   🔍 Header signature (HEX): {headerHex}");
                
                if (actualHeader.SequenceEqual(webmHeader))
                {
                    _output.WriteLine($"   ✅ Confirmed WebM format");
                }
                else
                {
                    _output.WriteLine($"   ⚠️ Not WebM format - continuing anyway");
                }
            }

            // Test the conversion method directly using AudioConversionHelper
            var convertedWavPath = await AudioConversionHelper.ConvertToWavWithFFmpeg(webmData, CancellationToken.None);

            // Verify the converted file  
            await VerifyWavFile(convertedWavPath, webmFileName);

            // Test with FFmpeg verification
            await VerifyWithFFmpeg(convertedWavPath, webmFileName);
        }
    }

    private async Task<string> TestConvertWebMToWav(byte[] webmData, string originalFileName, int testNumber)
    {
        // Create output file path
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputFileName = $"TEST_{testNumber:D2}_CONVERTED_{timestamp}_{Path.GetFileNameWithoutExtension(originalFileName)}.wav";
        string outputPath = Path.Combine(_testOutputDir, outputFileName);

        _output.WriteLine($"   🎯 Converting to: {outputPath}");

        // Create temporary WebM file for conversion
        string tempWebMFile = Path.GetTempFileName().Replace(".tmp", ".webm");
        string tempWavFile = Path.GetTempFileName().Replace(".tmp", ".wav");

        try
        {
            // Write WebM data to temp file
            await File.WriteAllBytesAsync(tempWebMFile, webmData);
            _output.WriteLine($"   📝 Temporary WebM created: {tempWebMFile}");

            // Use FFmpeg to convert (same as the service)
            bool conversionSuccess = await ConvertWithFFmpeg(tempWebMFile, tempWavFile);
            
            if (conversionSuccess && File.Exists(tempWavFile))
            {
                // Copy to test output directory for verification
                File.Copy(tempWavFile, outputPath, true);
                _output.WriteLine($"   ✅ Conversion successful! WAV saved to: {outputPath}");
                
                var wavInfo = new FileInfo(outputPath);
                _output.WriteLine($"   📊 Converted WAV size: {wavInfo.Length:N0} bytes");
                
                return outputPath;
            }
            else
            {
                _output.WriteLine($"   ❌ FFmpeg conversion failed");
                Assert.Fail($"FFmpeg conversion failed for {originalFileName}");
                return string.Empty;
            }
        }
        finally
        {
            // Clean up temp files
            try { if (File.Exists(tempWebMFile)) File.Delete(tempWebMFile); } catch { }
            try { if (File.Exists(tempWavFile)) File.Delete(tempWavFile); } catch { }
        }
    }

    private async Task<bool> ConvertWithFFmpeg(string inputOggFile, string outputWavFile)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputOggFile}\" -acodec pcm_s16le -ar 16000 -ac 1 \"{outputWavFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _output.WriteLine($"   🔧 FFmpeg command: ffmpeg {startInfo.Arguments}");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _output.WriteLine($"   ❌ Failed to start FFmpeg process");
                return false;
            }

            await process.WaitForExitAsync();
            
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            
            _output.WriteLine($"   📤 FFmpeg exit code: {process.ExitCode}");
            if (!string.IsNullOrEmpty(stderr))
            {
                _output.WriteLine($"   📋 FFmpeg stderr: {stderr}");
            }

            bool success = process.ExitCode == 0 && File.Exists(outputWavFile);
            _output.WriteLine($"   🎯 Conversion result: {(success ? "SUCCESS" : "FAILED")}");
            
            return success;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"   💥 FFmpeg exception: {ex.Message}");
            return false;
        }
    }

    private async Task VerifyWavFile(string wavPath, string originalFileName)
    {
        _output.WriteLine($"   🔍 Verifying WAV file: {Path.GetFileName(wavPath)}");

        // Check file exists and has content
        var fileInfo = new FileInfo(wavPath);
        Assert.True(fileInfo.Exists, $"Converted WAV file does not exist: {wavPath}");
        Assert.True(fileInfo.Length > 0, $"Converted WAV file is empty: {wavPath}");

        // Read and verify WAV header
        byte[] wavData = await File.ReadAllBytesAsync(wavPath);
        
        // Verify RIFF header
        if (wavData.Length >= 12)
        {
            var riffHeader = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
            var waveHeader = System.Text.Encoding.ASCII.GetString(wavData, 8, 4);
            
            _output.WriteLine($"   📋 WAV RIFF header: {riffHeader}");
            _output.WriteLine($"   📋 WAV WAVE header: {waveHeader}");
            
            Assert.Equal("RIFF", riffHeader);
            Assert.Equal("WAVE", waveHeader);
        }

        // Verify file size is reasonable (should be significantly larger than OGG for 16kHz PCM)
        Assert.True(wavData.Length > 1000, $"WAV file seems too small for {originalFileName}");
    }

    private async Task VerifyWithFFmpeg(string wavPath, string originalFileName)
    {
        _output.WriteLine($"   🔄 Verifying conversion with FFmpeg: {Path.GetFileName(wavPath)}");

        // Create expected file name (FFmpeg adds -converted)
        string expectedFileName = Path.GetFileNameWithoutExtension(originalFileName) + "-converted.wav";
        string expectedFilePath = Path.Combine(_testOutputDir, expectedFileName);

        // Use FFmpeg to compare the original and converted files
        bool comparisonSuccess = await CompareWithFFmpeg(wavPath, expectedFilePath);
        
        Assert.True(comparisonSuccess, $"FFmpeg verification failed for {originalFileName}");
    }

    private async Task<bool> CompareWithFFmpeg(string wavFile, string expectedFile)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{wavFile}\" -i \"{expectedFile}\" -lavfi \"[0:a][1:a]amix=inputs=2:duration=shortest[a]\" -map \"[a]\" -acodec pcm_s16le -ar 16000 -ac 1 \"{Path.Combine(_testOutputDir, "temp_mixed.wav")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _output.WriteLine($"   🔧 FFmpeg comparison command: ffmpeg {startInfo.Arguments}");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _output.WriteLine($"   ❌ Failed to start FFmpeg process for comparison");
                return false;
            }

            await process.WaitForExitAsync();
            
            var stderr = await process.StandardError.ReadToEndAsync();
            
            _output.WriteLine($"   📤 FFmpeg comparison exit code: {process.ExitCode}");
            if (!string.IsNullOrEmpty(stderr))
            {
                _output.WriteLine($"   📋 FFmpeg comparison stderr: {stderr}");
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"   💥 FFmpeg comparison exception: {ex.Message}");
            return false;
        }
    }
}
