using LeanKernel.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Scheduler;

/// <summary>
/// Provides dependency injection registration for LeanKernel scheduler services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers LeanKernel scheduler services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="config">The scheduler configuration to apply.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLeanKernelScheduler(this IServiceCollection services, SchedulerConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled)
        {
            return services;
        }

        services.AddSingleton<IOptions<SchedulerConfig>>(Options.Create(config));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CronScheduleEvaluator>();
        services.AddSingleton<TimeBoundaryService>();
        services.AddScoped<JobExecutor>();
        services.AddHostedService<SchedulerHostedService>();

        return services;
    }
}
