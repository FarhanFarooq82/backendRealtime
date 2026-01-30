using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Infrastructure.Persistence.Repositories;
using A3ITranslator.Infrastructure.Services.Orchestration;
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Services.Translation;
using A3ITranslator.Infrastructure.Services.Metrics;
using A3ITranslator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ✅ Persistence Layer
        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

        // ✅ Audio Processing Services (Singletons for stateful processing)
        services.AddSingleton<GoogleStreamingSTTService>();
        services.AddSingleton<AzureStreamingSTTService>();
        services.AddSingleton<IStreamingSTTService, STTOrchestrator>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<IAudioFeatureExtractor>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<OnnxAudioFeatureExtractor>>();
            var config = sp.GetRequiredService<IConfiguration>();
            string modelPath = config["SpeakerRecognition:ModelPath"] ?? Path.Combine(AppContext.BaseDirectory, "models", "speaker_recognition.onnx");
            
            logger.LogInformation("🔧 Loading ONNX speaker recognition model from: {Path}", modelPath);
            return new OnnxAudioFeatureExtractor(logger, modelPath);
        });
        services.AddSingleton<ISpeakerIdentificationService, SpeakerIdentificationService>();
        
        // ✅ Audio Test Services (Development/Debug)
        services.AddSingleton<AudioTestCollector>();

        // ✅ AI and GenAI Services (with Priority and Failover)
        // Register all GenAI providers
        services.AddSingleton<A3ITranslator.Infrastructure.Services.Gemini.GeminiGenAIService>();
        services.AddSingleton<A3ITranslator.Infrastructure.Services.Azure.AzureGenAIService>();
        services.AddSingleton<A3ITranslator.Infrastructure.Services.OpenAI.OpenAIGenAIService>();
        
        // Register GenAI orchestrator (wraps all providers with failover logic)
        services.AddSingleton<IGenAIService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GenAIOrchestrator>>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.ServiceOptions>>();
            
            // Collect all GenAI service implementations
            var providers = new List<IGenAIService>
            {
                sp.GetRequiredService<A3ITranslator.Infrastructure.Services.Gemini.GeminiGenAIService>(),
                sp.GetRequiredService<A3ITranslator.Infrastructure.Services.Azure.AzureGenAIService>(),
                sp.GetRequiredService<A3ITranslator.Infrastructure.Services.OpenAI.OpenAIGenAIService>()
            };
            
            return new GenAIOrchestrator(logger, options, providers);
        });
        
        services.AddSingleton<IFactExtractionService, FactExtractionService>();
        
        // Add HttpClient for Gemini and OpenAI
        services.AddHttpClient("GeminiClient");
        services.AddHttpClient("OpenAIClient");

        // ✅ TTS Services
        services.AddSingleton<IStreamingTTSService, StreamingTTSService>();

        // ✅ Translation Services
        services.AddSingleton<ITranslationPromptService, TranslationPromptService>();
        services.AddSingleton<ITranslationOrchestrator, TranslationOrchestrator>();

        // ✅ Speaker Management Services (Unified Pattern)
        services.AddSingleton<ISpeakerManagementService, SpeakerManagementService>();

        // ✅ Orchestration Helpers
        services.AddSingleton<ITranscriptionManager, TranscriptionManager>();
        services.AddSingleton<ISpeakerSyncService, SpeakerSyncService>();
        services.AddSingleton<IConversationLifecycleManager, ConversationLifecycleManager>();
        services.AddSingleton<IConversationResponseService, ConversationResponseService>();
        services.AddSingleton<IFactService, FactService>();
        services.AddSingleton<ITranslationService, TranslationService>();

        // ✅ Conversation Orchestration (Main Pipeline)
        services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();

        // ✅ Metrics and Cost Logging
        services.AddSingleton<IMetricsService, CsvMetricsLogger>();

        // Note: IRealtimeNotificationService (SignalRNotificationService) is registered in Program.cs
        // as it's in the API layer and should not be referenced from Infrastructure

        return services;
    }
}
