using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler;

/// <summary>
/// Registers and manages proactive scheduled tasks.
/// Connects job definitions (from config) to the CronScheduler.
/// </summary>
public sealed class ProactiveTaskRunner
{
    private readonly IScheduler _scheduler;
    private readonly Jobs.WikiMaintenanceJob _wikiJob;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ProactiveTaskRunner> _logger;

    public ProactiveTaskRunner(
        IScheduler scheduler,
        Jobs.WikiMaintenanceJob wikiJob,
        IOptions<LeanKernelConfig> config,
        ILogger<ProactiveTaskRunner> logger)
    {
        _scheduler = scheduler;
        _wikiJob = wikiJob;
        _config = config.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_config.Scheduler.Enabled)
        {
            _logger.LogInformation("Scheduler disabled in configuration");
            return;
        }

        // Register built-in maintenance job
        await _scheduler.ScheduleAsync(
            "wiki-maintenance",
            _config.Scheduler.WikiMaintenanceCron,
            _wikiJob.ExecuteAsync,
            ct);

        _logger.LogInformation("Proactive tasks registered: {Jobs}",
            string.Join(", ", _scheduler.ListScheduledJobs()));
    }
}
