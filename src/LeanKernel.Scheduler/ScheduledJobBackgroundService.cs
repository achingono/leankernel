using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Scheduler;

/// <summary>
/// Background loop that executes due managed scheduled jobs.
/// </summary>
public sealed class ScheduledJobBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly IScheduledJobManager _jobManager;
    private readonly ILogger<ScheduledJobBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledJobBackgroundService" /> class.
    /// </summary>
    public ScheduledJobBackgroundService(
        IScheduledJobManager jobManager,
        ILogger<ScheduledJobBackgroundService> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _jobManager.InitializeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _jobManager.ProcessDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled job background loop iteration failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
