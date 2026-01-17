using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.Services;
using A3ITranslator.Application.Services.Speaker;
using A3ITranslator.Infrastructure.Persistence.Repositories;
using A3ITranslator.Infrastructure.Services.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace A3ITranslator.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Persistence
        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

        // Speaker Management Services (Unified Pattern)
        services.AddSingleton<ISpeakerManagementService, SpeakerManagementService>();
        services.AddSingleton<DataRouterService>();

        // Conversation Orchestration
        services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();

        // External Services (Keep existing ones here if moving them to this pattern)
        // ...

        return services;
    }
}
