using System.ComponentModel.DataAnnotations;

using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Configuration;

/// <summary>
/// Configures runtime behavior for scheduled learning jobs.
/// </summary>
public sealed class SchedulerRuntimeOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether scheduler processing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    [Range(5, 3600)]
    public int PollIntervalSeconds { get; set; } = 30;
}
