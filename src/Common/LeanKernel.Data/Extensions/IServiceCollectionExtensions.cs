namespace Microsoft.Extensions.DependencyInjection;

using LeanKernel.Data;
using LeanKernel.Data.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Provides extension methods for registering LeanKernel data services with the dependency injection container.
/// </summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="EntityContext"/> and its related interceptors in the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptionsBuilder"/> for the <see cref="EntityContext"/>.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddEntityContext(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsAction)
    {
        services.AddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, SenderInterceptor>();

        services.AddDbContext<EntityContext>((sp, option) =>
        {
            var interceptors = sp.GetServices<ISaveChangesInterceptor>();
            option.AddInterceptors(interceptors);
            optionsAction(option);
        });

        return services.AddDbContextFactory<EntityContext>(
            (sp, option) =>
            {
                var interceptors = sp.GetServices<ISaveChangesInterceptor>();
                option.AddInterceptors(interceptors);
                optionsAction(option);
            },
            ServiceLifetime.Scoped);
    }
}