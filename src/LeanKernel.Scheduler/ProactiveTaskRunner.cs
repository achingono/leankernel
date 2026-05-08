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
    private readonly Jobs.ChatFactScrubJob _chatFactScrubJob;
    private readonly Jobs.ModelLimitSyncJob _syncJob;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ProactiveTaskRunner> _logger;

    public ProactiveTaskRunner(
        IScheduler scheduler,
        Jobs.WikiMaintenanceJob wikiJob,
        Jobs.ChatFactScrubJob chatFactScrubJob,
        Jobs.ModelLimitSyncJob syncJob,
        IOptions<LeanKernelConfig> config,
        ILogger<ProactiveTaskRunner> logger)
    {
        _scheduler = scheduler;
        _wikiJob = wikiJob;
        _chatFactScrubJob = chatFactScrubJob;
        _syncJob = syncJob;
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

        await _scheduler.ScheduleAsync(
            "chat-fact-scrub",
            _config.Scheduler.ChatFactScrubCron,
            _chatFactScrubJob.ExecuteAsync,
            ct);

        // Phase 4 — model limit sync (only when routing is enabled)
        if (_config.Routing.Enabled)
        {
            await _scheduler.ScheduleAsync(
                "model-limit-sync",
                _config.Routing.ModelLimitSyncCron,
                _syncJob.ExecuteAsync,
                ct);
        }

        _logger.LogInformation("Proactive tasks registered: {Jobs}",
            string.Join(", ", _scheduler.ListScheduledJobs()));
    }
}
