using System.ComponentModel.DataAnnotations;

namespace LeanKernel.Services.Learning.Configuration;

/// <summary>
/// Configures runtime behavior for the asynchronous learning worker.
/// </summary>
public sealed class LearningRuntimeOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether learning is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the bounded queue capacity used for completed turn events.
    /// </summary>
    [Range(1, 10000)]
    public int QueueCapacity { get; set; } = 512;
}
