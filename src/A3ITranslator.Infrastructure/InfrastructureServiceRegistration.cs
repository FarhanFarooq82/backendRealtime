using A3ITranslator.Application.Domain.Interfaces;
using A3ITranslator.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace A3ITranslator.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Persistence
        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

        // External Services (Keep existing ones here if moving them to this pattern)
        // ...

        return services;
    }
}
