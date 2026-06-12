namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures coordinator-worker orchestration behavior.
/// </summary>
public sealed class OrchestrationConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether orchestration is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of workers the coordinator may invoke concurrently.
    /// </summary>
    public int MaxWorkerConcurrency { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum orchestration depth allowed for one run.
    /// </summary>
    public int MaxOrchestrationDepth { get; set; } = 2;

    /// <summary>
    /// Gets or sets the timeout budget applied to each worker invocation.
    /// </summary>
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the configured worker definitions.
    /// </summary>
    public List<WorkerDefinition> Workers { get; set; } = [];
}

/// <summary>
/// Describes one orchestration worker.
/// </summary>
public sealed class WorkerDefinition
{
    /// <summary>
    /// Gets or sets the unique worker name used for tool registration.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the worker purpose shown to the coordinator.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Gets or sets the model used when the worker executes.
    /// </summary>
    public string Model { get; set; } = "small";

    /// <summary>
    /// Gets or sets the worker system prompt.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the allowlisted tool names for the worker.
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// Gets or sets the allowlisted tool categories for the worker.
    /// </summary>
    public List<string> AllowedCategories { get; set; } = [];

    /// <summary>
    /// Gets or sets the optional retrieval or execution scope for the worker.
    /// </summary>
    public string? Scope { get; set; }
}
