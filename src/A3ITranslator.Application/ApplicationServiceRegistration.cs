using A3ITranslator.Application.Services.Frontend;
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

        // Frontend services (Singleton for use with Singleton orchestrators)
        services.AddSingleton<IFrontendConversationItemService, FrontendConversationItemService>();

        // Future: Add Validators, Behaviors here

        return services;
    }
}
