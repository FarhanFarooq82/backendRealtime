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
        services.AddSingleton<IAudioFeatureExtractor, AudioFeatureExtractor>();
        services.AddSingleton<ISpeakerIdentificationService, SpeakerIdentificationService>();
        
        // ✅ Audio Test Services (Development/Debug)
        services.AddSingleton<AudioTestCollector>();

        // ✅ AI and GenAI Services
        services.AddSingleton<IGenAIService, AzureGenAIService>();
        services.AddSingleton<IFactExtractionService, FactExtractionService>();

        // ✅ TTS Services
        services.AddSingleton<IStreamingTTSService, StreamingTTSService>();

        // ✅ Translation Services
        services.AddSingleton<ITranslationPromptService, TranslationPromptService>();
        services.AddSingleton<ITranslationOrchestrator, TranslationOrchestrator>();

        // ✅ Speaker Management Services (Unified Pattern)
        services.AddSingleton<ISpeakerManagementService, SpeakerManagementService>();

        // ✅ Conversation Orchestration (Main Pipeline)
        services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();

        // ✅ Data Routing Service
        services.AddSingleton<DataRouterService>();

        // ✅ Metrics and Cost Logging
        services.AddSingleton<IMetricsService, CsvMetricsLogger>();

        // Note: IRealtimeNotificationService (SignalRNotificationService) is registered in Program.cs
        // as it's in the API layer and should not be referenced from Infrastructure

        return services;
    }
}
