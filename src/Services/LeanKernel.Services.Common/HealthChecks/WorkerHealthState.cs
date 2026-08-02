namespace LeanKernel.Services.Common.HealthChecks;

/// <summary>
/// Singleton health state tracker for background workers.
/// Workers update their last-healthy timestamp and health checks verify freshness.
/// </summary>
public sealed class WorkerHealthState
{
    private readonly object _lock = new();
    private DateTime _learningWorkerLastHealthyUtc = DateTime.MinValue;
    private DateTime _schedulerWorkerLastHealthyUtc = DateTime.MinValue;

    /// <summary>
    /// Records a successful poll cycle for the learning worker.
    /// </summary>
    public void MarkLearningWorkerHealthy()
    {
        lock (_lock)
        {
            _learningWorkerLastHealthyUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Records a successful poll cycle for the scheduler.
    /// </summary>
    public void MarkSchedulerHealthy()
    {
        lock (_lock)
        {
            _schedulerWorkerLastHealthyUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns the age of the last learning worker heartbeat.
    /// </summary>
    public TimeSpan LearningWorkerHeartbeatAge
    {
        get
        {
            lock (_lock)
            {
                return _learningWorkerLastHealthyUtc == DateTime.MinValue
                    ? TimeSpan.MaxValue
                    : DateTime.UtcNow - _learningWorkerLastHealthyUtc;
            }
        }
    }

    /// <summary>
    /// Returns the age of the last scheduler heartbeat.
    /// </summary>
    public TimeSpan SchedulerHeartbeatAge
    {
        get
        {
            lock (_lock)
            {
                return _schedulerWorkerLastHealthyUtc == DateTime.MinValue
                    ? TimeSpan.MaxValue
                    : DateTime.UtcNow - _schedulerWorkerLastHealthyUtc;
            }
        }
    }
}