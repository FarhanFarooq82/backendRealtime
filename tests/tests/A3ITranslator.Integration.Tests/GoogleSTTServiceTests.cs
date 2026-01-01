using A3ITranslator.Infrastructure.Services.Google;
using A3ITranslator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;

namespace A3ITranslator.Integration.Tests.Services.Google;

/// <summary>
/// Unit tests for Google Cloud Speech-to-Text service implementation
/// Tests verify configuration, language support, and core functionality
/// Language list verified against: https://cloud.google.com/speech-to-text/docs/languages
/// </summary>
public class GoogleSTTServiceTests
{
    private readonly Mock<ILogger<GoogleSTTService>> _mockLogger;
    private readonly ServiceOptions _serviceOptions;

    public GoogleSTTServiceTests()
    {
        _mockLogger = new Mock<ILogger<GoogleSTTService>>();
        _serviceOptions = new ServiceOptions
        {
            Google = new GoogleOptions
            {
                CredentialsPath = "/path/to/credentials.json",
                ProjectId = "test-project",
                STTModel = "chirp_2"  // Include the model configuration for testing
            }
        };
    }

    [Fact]
    public void GoogleSTTService_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);

        // Act
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void SupportsLanguageDetection_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var result = service.SupportsLanguageDetection;

        // Assert
        Assert.True(result, "Google STT supports language detection");
    }

    [Fact]
    public void RequiresAudioConversion_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var result = service.RequiresAudioConversion;

        // Assert
        Assert.False(result, "Google STT natively supports multiple audio formats without conversion");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldReturnStatus()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act & Assert
        // Note: May return false due to credentials but should not throw
        var result = await service.CheckHealthAsync();
        
        // Health check should execute without exception
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetSupportedLanguages_ShouldReturnValidLanguageList()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var languages = service.GetSupportedLanguages();

        // Assert
        Assert.NotNull(languages);
        Assert.NotEmpty(languages);
        
        // Verify major world languages are supported
        Assert.Contains(languages, lang => lang.Key == "en-US" && lang.Value == "English (United States)");
        Assert.Contains(languages, lang => lang.Key == "es-ES" && lang.Value == "Spanish (Spain)");
        Assert.Contains(languages, lang => lang.Key == "fr-FR" && lang.Value == "French (France)");
        Assert.Contains(languages, lang => lang.Key == "de-DE" && lang.Value == "German (Germany)");
        Assert.Contains(languages, lang => lang.Key == "zh-CN" && lang.Value == "Chinese (Simplified, China)");
        Assert.Contains(languages, lang => lang.Key == "ja-JP" && lang.Value == "Japanese (Japan)");
        Assert.Contains(languages, lang => lang.Key == "ko-KR" && lang.Value == "Korean (South Korea)");
    }

    [Fact]
    public void GetSupportedLanguages_ShouldReturnVerifiedCount()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var languages = service.GetSupportedLanguages();

        // Assert
        // Verified count based on official Google Cloud documentation
        Assert.True(languages.Count >= 60 && languages.Count <= 70, 
            $"Expected 60-70 verified languages, got {languages.Count}");
    }

    [Fact]
    public void GetSupportedLanguages_ShouldHaveValidFormat()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var languages = service.GetSupportedLanguages();

        // Assert
        foreach (var language in languages)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.Key), "Language code should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(language.Value), "Language name should not be empty");
            Assert.Matches(@"^[a-z]{2}-[A-Z]{2}$", language.Key); // Format: xx-XX
        }
    }

    [Fact]
    public void GetServiceName_ShouldReturnCorrectName()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var serviceName = service.GetServiceName();

        // Assert
        Assert.Equal("Google STT", serviceName);
    }

    [Fact]
    public void GetSupportedLanguages_ShouldIncludeRegionalVariants()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var languages = service.GetSupportedLanguages();

        // Assert
        // Check for regional variants of major languages
        var englishVariants = languages.Where(l => l.Key.StartsWith("en-")).ToList();
        var spanishVariants = languages.Where(l => l.Key.StartsWith("es-")).ToList();
        
        Assert.True(englishVariants.Count >= 3, "Should have multiple English variants (US, UK, AU, etc.)");
        Assert.True(spanishVariants.Count >= 2, "Should have multiple Spanish variants (ES, MX, etc.)");
    }

    [Fact]
    public void ServiceName_ShouldReturnCorrectName()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act
        var serviceName = service.GetType().Name;

        // Assert
        Assert.Equal("GoogleSTTService", serviceName);
    }

    [Fact]
    public void GetSupportedLanguages_ShouldHandleConfiguration()
    {
        // Arrange
        var options = Options.Create(_serviceOptions);
        var service = new GoogleSTTService(options, _mockLogger.Object);

        // Act & Assert - Should not throw
        var languages = service.GetSupportedLanguages();
        Assert.NotNull(languages);
    }
}
