using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics;

/// <summary>
/// Extension methods for configuring diagnostics in the dependency injection container.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Adds LeanKernel diagnostics services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The diagnostics configuration.</param>
    /// <returns>The service collection after adding diagnostics services.</returns>
    public static IServiceCollection AddLeanKernelDiagnostics(
        this IServiceCollection services,
        DiagnosticsConfig config)
    {
        services.AddSingleton(Options.Create(config));
        services.AddSingleton<DiagnosticsCollector>();
        services.AddSingleton<IContextDiagnosticsService, ContextDiagnosticsService>();
        services.AddSingleton<LeanKernelMetrics>();

        return services;
    }
}
