using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using A3ITranslator.API.Hubs;
using A3ITranslator.API.Services;
using A3ITranslator.Application.Services;
using A3ITranslator.Infrastructure.Configuration;
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

// ✅ API-Specific Services (Only what belongs in API layer)
builder.Services.AddSingleton<IRealtimeNotificationService, SignalRNotificationService>();

// Add API Controllers (keeping both LanguagesController and TranslationController)
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

// Configure Kestrel for both HTTP and HTTPS
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenLocalhost(8000); // HTTP
    serverOptions.ListenLocalhost(5001, listenOptions =>
    {
        listenOptions.UseHttps(); // HTTPS
    });
});

// CORS for SignalR - Updated to include both ports
builder.Services.AddCors(options =>
{
    options.AddPolicy("RealtimeOnly", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000", 
            "http://localhost:8000",  // Backend HTTP
            "https://localhost:5001", // Backend HTTPS
            "http://127.0.0.1:3000",
            "http://127.0.0.1:8000",
            "https://127.0.0.1:5001"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors("RealtimeOnly");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// ✅ Don't redirect HTTP to HTTPS - allow both
// app.UseHttpsRedirection(); // Commented out to allow frontend flexibility

app.UseAuthorization();
app.MapControllers();

// Map SignalR Hub - Match frontend expectation
app.MapHub<HubClient>("/audio-hub");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "realtime-audio" }));

// ✅ Application startup confirmation
Console.WriteLine("🚀 A3I Realtime Translator API started successfully!");
Console.WriteLine("📡 Available endpoints:");
Console.WriteLine("   HTTP:  http://localhost:8000");
Console.WriteLine("   HTTPS: https://localhost:5001");
Console.WriteLine("🌐 SignalR Hub available at: /audio-hub");
Console.WriteLine("📡 Languages API available at: /api/Languages");
Console.WriteLine("🔤 Translation API available at: /api/translate-text");
Console.WriteLine("💓 Health check available at: /health");

// ✅ CRITICAL: Keep the application running
app.Run();
