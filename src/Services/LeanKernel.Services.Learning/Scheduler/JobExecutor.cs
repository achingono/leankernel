namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Dispatches scheduled jobs to registered handlers based on job type.
/// </summary>
public sealed class JobExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    public JobExecutor(IServiceScopeFactory scopeFactory, ILogger<JobExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Executes the given scheduled job by dispatching to its registered handler.
    /// </summary>
    /// <param name="job">The scheduled job entity.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(LeanKernel.Entities.ScheduledJobEntity job, CancellationToken ct = default)
    {
        IJobHandler? handler = job.JobType switch
        {
            "DreamCycle" => ActivatorUtilities.CreateInstance<DreamCycleJobHandler>(_scopeFactory.CreateScope().ServiceProvider),
            _ => null,
        };

        if (handler == null)
        {
            _logger.LogWarning("No handler registered for job type {JobType}", job.JobType);
            return;
        }

        try
        {
            await handler.ExecuteAsync(job.Id, job.ConfigurationJson, ct);
            _logger.LogInformation("Executed job {JobName} ({JobType})", job.Name, job.JobType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute job {JobName} ({JobType})", job.Name, job.JobType);
        }
    }
}