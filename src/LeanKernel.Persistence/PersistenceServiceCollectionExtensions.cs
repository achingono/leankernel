using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Persistence.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides dependency injection registration for LeanKernel persistence services.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers LeanKernel persistence services and the EF Core DbContext factory.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="config">The database configuration to use for PostgreSQL.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLeanKernelPersistence(this IServiceCollection services, DatabaseConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ConnectionString);

        services.AddSingleton<DbCommandActivityInterceptor>();
        services.AddDbContextFactory<LeanKernelDbContext>((provider, options) =>
        {
            options.UseNpgsql(config.ConnectionString);

            var interceptor = provider.GetService<DbCommandActivityInterceptor>();
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });

        services.AddScoped<PostgresSessionStore>();
        services.AddScoped<ISessionStore>(provider => provider.GetRequiredService<PostgresSessionStore>());
        services.AddSingleton<PostgresDiagnosticsSink>();
        services.AddSingleton<IDiagnosticsSink>(provider => provider.GetRequiredService<PostgresDiagnosticsSink>());

        return services;
    }
}
