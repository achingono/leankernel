using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
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
