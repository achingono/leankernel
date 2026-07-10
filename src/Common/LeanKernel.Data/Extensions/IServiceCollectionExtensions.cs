using LeanKernel.Data;
using LeanKernel.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering LeanKernel data services with the dependency injection container.
/// </summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="EntityContext"/> and its related interceptors in the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="optionsAction">An action to configure the <see cref="DbContextOptions"/> for the <see cref="DbContext"/>.</param>
    public static IServiceCollection AddEntityContext(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsAction)
    {
        //services.TryAddTransient<IDbContextValidator, EntityContextValidator>();
        services.AddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var interceptors = scope.ServiceProvider.GetServices<ISaveChangesInterceptor>();
        return services.AddDbContext<EntityContext>(option =>
        {
            option.AddInterceptors(interceptors);
            optionsAction?.Invoke(option);
        });
    }
}

