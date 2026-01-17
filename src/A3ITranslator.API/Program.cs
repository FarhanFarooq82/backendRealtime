using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options; // ✅ Add this for IOptions
using A3ITranslator.API.Hubs;
using A3ITranslator.API.Services;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker; // ✅ Add for clean speaker services
using A3ITranslator.Application.Domain.Interfaces; // ✅ Add for ISessionRepository
using A3ITranslator.Infrastructure.Persistence.Repositories; // ✅ Add for InMemorySessionRepository
using A3ITranslator.Infrastructure.Services.Audio;
using A3ITranslator.Infrastructure.Services;
using A3ITranslator.Infrastructure.Services.Azure;
using A3ITranslator.Infrastructure.Services.Translation; // 🆕 Add for translation services
using A3ITranslator.Infrastructure.Configuration; // ✅ Add this for ServiceOptions
using A3ITranslator.Application;
using A3ITranslator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ✅ Log environment for debugging
Console.WriteLine($"🔧 Running in environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"🔧 Configuration sources: {string.Join(", ", builder.Configuration.Sources.Select(s => s.GetType().Name))}");

// ✅ CRITICAL: Configure ServiceOptions binding
builder.Services.Configure<ServiceOptions>(
    builder.Configuration.GetSection(ServiceOptions.SectionName));

// ✅ Clean Architecture Service Registrations
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Add API Controllers
builder.Services.AddControllers();

// Add API Explorer for development
builder.Services.AddEndpointsApiExplorer();

// SignalR Configuration
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    options.StreamBufferCapacity = 50;
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

// 🔥 CRITICAL FIX: Register Domain Repository for Clean Architecture
builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

// 🔥 CRITICAL FIX: STT services must be SINGLETONS to maintain chunk accumulation state
// Scoped services get recreated for each request, losing accumulated audio buffers
builder.Services.AddSingleton<GoogleStreamingSTTService>(); // ✅ SINGLETON: Maintains chunk state
builder.Services.AddSingleton<AzureStreamingSTTService>();  // ✅ SINGLETON: Maintains chunk state
builder.Services.AddSingleton<IStreamingSTTService, STTOrchestrator>(); // ✅ SINGLETON: Uses singleton services

// 🎵 Audio Test Collector - DEBUG ONLY service for testing audio reception
builder.Services.AddSingleton<AudioTestCollector>(); // ✅ SINGLETON: Accumulates audio chunks for testing

// 🔧 Non-stateful services upgraded to Singleton for Orchestrator compatibility
builder.Services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
builder.Services.AddSingleton<IAudioFeatureExtractor, AudioFeatureExtractor>(); // ✅ SINGLETON: Feature extraction service
builder.Services.AddSingleton<ISpeakerIdentificationService, SpeakerIdentificationService>();
builder.Services.AddSingleton<IRealtimeNotificationService, SignalRNotificationService>();
builder.Services.AddSingleton<IGenAIService, AzureGenAIService>();
builder.Services.AddSingleton<IStreamingTTSService, StreamingTTSService>();
builder.Services.AddSingleton<IFactExtractionService, FactExtractionService>();

// 🆕 Translation Services
builder.Services.AddSingleton<ITranslationPromptService, TranslationPromptService>();
builder.Services.AddSingleton<ITranslationOrchestrator, TranslationOrchestrator>();

// ✅ Clean Services already registered in InfrastructureServiceRegistration
builder.Services.AddSingleton<DataRouterService>();

// TODO: Add these when they exist
// builder.Services.AddScoped<IStreamingTranslationService, StreamingTranslationService>();

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

// CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("RealtimeOnly", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://yourdomain.com", "http://127.0.0.1:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("RealtimeOnly");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Add API routing
app.MapControllers();
app.MapHub<HubClient>("/audio-hub");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "realtime-audio" }));

app.Run();