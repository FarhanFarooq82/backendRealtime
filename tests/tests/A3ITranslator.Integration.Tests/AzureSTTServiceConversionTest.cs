using A3ITranslator.Infrastructure.Configuration;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace A3ITranslator.Integration.Tests;

/// <summary>
/// Test to verify that AudioConversionHelper.ConvertToWavWithFFmpeg uses the correct conversion method
/// Tests the audio conversion helper that is used by AzureSTTService
/// </summary>
public class AzureSTTServiceConversionTest
{
    private readonly ITestOutputHelper _output;
    private readonly AzureSTTService _azureSTTService;

    public AzureSTTServiceConversionTest(ITestOutputHelper output)
    {
        _output = output;
        
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
    public async Task ConvertToWavWithFFmpeg_ShouldUseCorrectFFmpegConversion()
    {
        // Arrange - Get a WebM file from the root directory
        _output.WriteLine("=== Testing AudioConversionHelper.ConvertToWavWithFFmpeg ===");
        var rootWebMFiles = Directory.GetFiles("/Users/farhanfarooq/Documents/GitHub/A3ITranslator", "*.ogg");
        
        if (rootWebMFiles.Length == 0)
        {
            _output.WriteLine("❌ No WebM files found in root directory for testing");
            Assert.Fail("No WebM files available for testing");
            return;
        }

        var testFile = rootWebMFiles.First();
        _output.WriteLine($"🧪 Testing conversion with file: {Path.GetFileName(testFile)}");

        // Read the WebM file
        var webmBytes = await File.ReadAllBytesAsync(testFile);
        _output.WriteLine($"📊 File size: {webmBytes.Length:N0} bytes");

        // Verify it's WebM format
        if (webmBytes.Length >= 4)
        {
            var webmHeader = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
            var actualHeader = webmBytes.Take(4).ToArray();
            var isWebM = actualHeader.SequenceEqual(webmHeader);
            _output.WriteLine($"📋 WebM format detected: {isWebM}");
            
            if (!isWebM)
            {
                _output.WriteLine($"⚠️  Expected WebM header but got: {BitConverter.ToString(actualHeader)}");
            }
        }

        // Act - Use the audio conversion helper directly
        var convertedFilePath = await AudioConversionHelper.ConvertToWavWithFFmpeg(webmBytes, CancellationToken.None);

        // Assert - Verify the conversion worked
        Assert.NotNull(convertedFilePath);
        Assert.True(File.Exists(convertedFilePath), $"Converted file should exist: {convertedFilePath}");

        var convertedFileInfo = new FileInfo(convertedFilePath);
        _output.WriteLine($"✅ Converted file created: {convertedFilePath}");
        _output.WriteLine($"📊 Converted file size: {convertedFileInfo.Length:N0} bytes");

        // Verify it's a valid WAV file
        using var fileStream = File.OpenRead(convertedFilePath);
        var header = new byte[12];
        await fileStream.ReadAsync(header, 0, 12);

        var riffHeader = System.Text.Encoding.ASCII.GetString(header, 0, 4);
        var formatHeader = System.Text.Encoding.ASCII.GetString(header, 8, 4);

        Assert.Equal("RIFF", riffHeader);
        Assert.Equal("WAVE", formatHeader);
        
        _output.WriteLine($"✅ WAV format verified: RIFF={riffHeader}, WAVE={formatHeader}");

        // Clean up
        try
        {
            if (File.Exists(convertedFilePath))
            {
                File.Delete(convertedFilePath);
                _output.WriteLine($"🗑️  Cleaned up temporary file: {convertedFilePath}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"⚠️  Could not clean up file: {ex.Message}");
        }

        _output.WriteLine("🎯 SUCCESS: AzureSTTService.ConvertToWavWithFFmpeg works correctly!");
    }

    [Fact]
    public async Task CompareConversionMethods_ShouldProduceSimilarResults()
    {
        // Arrange - Get a WebM file
        var rootWebMFiles = Directory.GetFiles("/Users/farhanfarooq/Documents/GitHub/A3ITranslator", "*.ogg");
        
        if (rootWebMFiles.Length == 0)
        {
            _output.WriteLine("❌ No WebM files found for comparison test");
            Assert.Fail("No WebM files available for testing");
            return;
        }

        var testFile = rootWebMFiles.First();
        var webmBytes = await File.ReadAllBytesAsync(testFile);
        
        _output.WriteLine($"🔄 Comparing conversion methods with: {Path.GetFileName(testFile)}");

        // Method 1: Direct FFmpeg conversion (like our test)
        var tempWebMFile1 = Path.GetTempFileName().Replace(".tmp", ".webm");
        var tempWavFile1 = Path.GetTempFileName().Replace(".tmp", "_direct.wav");
        await File.WriteAllBytesAsync(tempWebMFile1, webmBytes);
        
        var directSuccess = await ConvertWithDirectFFmpeg(tempWebMFile1, tempWavFile1);
        
        // Method 2: AudioConversionHelper conversion
        var serviceConvertedFile = await AudioConversionHelper.ConvertToWavWithFFmpeg(webmBytes, CancellationToken.None);

        // Compare results
        var directFileExists = File.Exists(tempWavFile1);
        var serviceFileExists = File.Exists(serviceConvertedFile);
        
        _output.WriteLine($"📊 Direct FFmpeg conversion: {(directSuccess ? "Success" : "Failed")}");
        _output.WriteLine($"📊 Service conversion: {(serviceFileExists ? "Success" : "Failed")}");

        if (directFileExists && serviceFileExists)
        {
            var directSize = new FileInfo(tempWavFile1).Length;
            var serviceSize = new FileInfo(serviceConvertedFile).Length;
            
            _output.WriteLine($"📊 Direct conversion size: {directSize:N0} bytes");
            _output.WriteLine($"📊 Service conversion size: {serviceSize:N0} bytes");
            
            // Sizes should be reasonably similar (within 10%)
            var sizeDifference = Math.Abs(directSize - serviceSize) / (double)Math.Max(directSize, serviceSize);
            _output.WriteLine($"📊 Size difference: {sizeDifference:P1}");
            
            Assert.True(sizeDifference < 0.1, $"Conversion sizes should be similar. Difference: {sizeDifference:P1}");
        }

        Assert.True(directSuccess == serviceFileExists, "Both conversion methods should have the same success result");

        // Clean up
        try
        {
            if (File.Exists(tempWebMFile1)) File.Delete(tempWebMFile1);
            if (File.Exists(tempWavFile1)) File.Delete(tempWavFile1);
            if (File.Exists(serviceConvertedFile)) File.Delete(serviceConvertedFile);
        }
        catch { }

        _output.WriteLine("🎯 SUCCESS: Both conversion methods work identically!");
    }

    private async Task<bool> ConvertWithDirectFFmpeg(string inputFile, string outputFile)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputFile}\" -acodec pcm_s16le -ar 16000 -ac 1 \"{outputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(outputFile);
        }
        catch
        {
            return false;
        }
    }
}
