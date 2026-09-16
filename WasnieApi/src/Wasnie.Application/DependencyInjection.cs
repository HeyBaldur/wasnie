using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Behaviors;

namespace Wasnie.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        // Favorites (KAN-64): one provider per entity type. A new section registers a provider here — nothing else.
        services.AddScoped<Features.Favorites.IFavoriteEntityProvider, Features.Favorites.PayeeFavoriteProvider>();
        services.AddScoped<Features.Favorites.IFavoriteEntityProvider, Features.Favorites.PlanFavoriteProvider>();
        services.AddScoped<Features.Favorites.FavoriteProviders>();

        return services;
    }
}
