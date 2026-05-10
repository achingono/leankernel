using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler;

/// <summary>
/// Interface for job wrappers that can be executed asynchronously.
/// </summary>
public interface IAsyncJob
{
    /// <summary>
    /// Executes the operation.
    /// </summary>
    Task ExecuteAsync(CancellationToken ct);
}

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
    private readonly IAsyncJob _userProfileSyncJob;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ProactiveTaskRunner> _logger;

    /// <summary>
    /// Represents the proactive task runner.
    /// </summary>
    public ProactiveTaskRunner(
        IScheduler scheduler,
        Jobs.WikiMaintenanceJob wikiJob,
        Jobs.ChatFactScrubJob chatFactScrubJob,
        Jobs.ModelLimitSyncJob syncJob,
        IAsyncJob userProfileSyncJob,
        IOptions<LeanKernelConfig> config,
        ILogger<ProactiveTaskRunner> logger)
    {
        _scheduler = scheduler;
        _wikiJob = wikiJob;
        _chatFactScrubJob = chatFactScrubJob;
        _syncJob = syncJob;
        _userProfileSyncJob = userProfileSyncJob;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes the start async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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

        // User profile sync learns durable identity updates from wiki facts.
        await _scheduler.ScheduleAsync(
            "user-profile-sync",
            _config.Scheduler.UserProfileSyncCron,
            _userProfileSyncJob.ExecuteAsync,
            ct);

        // Model-limit sync only runs when intelligent routing is enabled.
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
