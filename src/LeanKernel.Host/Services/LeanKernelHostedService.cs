using LeanKernel.Commander;

namespace LeanKernel.Host.Services;

/// <summary>
/// Background service that starts the channel router and scheduler.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LeanKernelHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOnboardingStateStore _onboardingState;
    private readonly ILogger<LeanKernelHostedService> _logger;

    /// <summary>
    /// Represents the lean kernel hosted service.
    /// </summary>
    public LeanKernelHostedService(
        IServiceProvider services,
        IOnboardingStateStore onboardingState,
        ILogger<LeanKernelHostedService> logger)
    {
        _services = services;
        _onboardingState = onboardingState;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LeanKernel engine starting...");
        var waitingLogged = false;
        while (!stoppingToken.IsCancellationRequested && !await _onboardingState.IsCompletedAsync(stoppingToken))
        {
            if (!waitingLogged)
            {
                _logger.LogInformation("Onboarding not complete yet; channel startup deferred");
                waitingLogged = true;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
            return;

        var router = _services.GetRequiredService<ChannelRouter>();
        await router.StartAsync(stoppingToken);

        var taskRunner = _services.GetRequiredService<LeanKernel.Scheduler.ProactiveTaskRunner>();
        await taskRunner.StartAsync(stoppingToken);

        _logger.LogInformation("LeanKernel engine running. Waiting for messages.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LeanKernel engine shutting down...");
        }

        await router.StopAsync(stoppingToken);
    }
}
