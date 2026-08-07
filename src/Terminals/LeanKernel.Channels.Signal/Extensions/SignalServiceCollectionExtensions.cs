using LeanKernel;
using LeanKernel.Channels.Signal.HealthChecks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Service collection extensions for the Signal channel.
/// </summary>
public static class SignalServiceCollectionExtensions
{
    /// <summary>
    /// Configures and registers Signal socket worker health monitoring, including options validation
    /// enforcing that <c>WorkerUnhealthyThresholdSeconds</c> is greater than <c>WorkerDegradedThresholdSeconds</c>,
    /// which in turn must be greater than the client receive deadline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSignalWorkerHealthCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SignalSettings>()
            .BindConfiguration("Signal")
            .Validate(
                static settings => settings.WorkerUnhealthyThresholdSeconds > settings.WorkerDegradedThresholdSeconds,
                "WorkerUnhealthyThresholdSeconds must be greater than WorkerDegradedThresholdSeconds.")
            .Validate(
                static settings => settings.WorkerUnhealthyErrorThreshold > settings.WorkerConsecutiveErrorThreshold,
                "WorkerUnhealthyErrorThreshold must be greater than WorkerConsecutiveErrorThreshold.")
            .Validate(
                static settings => settings.WorkerConsecutiveErrorThreshold >= 1,
                "WorkerConsecutiveErrorThreshold must be at least 1.")
            .Validate(
                static settings => settings.WorkerDegradedThresholdSeconds > ResolveMinimumDegradedThresholdSeconds(settings),
                "WorkerDegradedThresholdSeconds must be greater than the Signal client receive deadline plus reconnect delay.")
            .ValidateOnStart();

        services.AddSingleton<ISocketWorkerHealthProvider>(
            static provider => provider.GetRequiredService<SocketTransportClient>());
        services.AddHealthChecks().AddCheck<SocketWorkerHealthCheck>(
            Constants.Healthchecks.SocketWorker,
            tags: [Constants.Healthchecks.SocketWorker]);

        return services;
    }

    private static int ResolveClientReceiveDeadlineSeconds(SignalSettings settings) =>
        settings.ReceiveClientDeadlineSeconds > 0
            ? settings.ReceiveClientDeadlineSeconds
            : settings.ReceiveTimeoutSeconds + 5;

    private static int ResolveMinimumDegradedThresholdSeconds(SignalSettings settings) =>
        ResolveClientReceiveDeadlineSeconds(settings) + Math.Max(1, settings.ReconnectDelaySeconds);
}
