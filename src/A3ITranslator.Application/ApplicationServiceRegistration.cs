using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace A3ITranslator.Application;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        // Future: Add Validators, Behaviors here

        return services;
    }
}
