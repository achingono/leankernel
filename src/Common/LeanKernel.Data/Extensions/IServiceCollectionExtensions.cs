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
        services.TryAddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
        services.TryAddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();

        static void ConfigureFactoryOptions(DbContextOptionsBuilder option, Action<DbContextOptionsBuilder> configure)
        {
            configure(option);
        }

        static void ConfigureContextOptions(IServiceProvider sp, DbContextOptionsBuilder option, Action<DbContextOptionsBuilder> configure)
        {
            var interceptors = sp.GetServices<ISaveChangesInterceptor>();
            option.AddInterceptors(interceptors);
            configure(option);
        }

        services.AddDbContextFactory<EntityContext>(
            (_, option) => ConfigureFactoryOptions(option, optionsAction),
            ServiceLifetime.Scoped);

        return services.AddDbContext<EntityContext>((sp, option) =>
            ConfigureContextOptions(sp, option, optionsAction));
    }
}