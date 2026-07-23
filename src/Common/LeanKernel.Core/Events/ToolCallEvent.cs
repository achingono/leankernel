namespace LeanKernel.Events;

/// <summary>
/// Represents a tool invocation event in the append-only event spine.
/// Captures the tool name, arguments, result, error state, and duration.
/// </summary>
public sealed record ToolCallEvent : IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope providing partitioning and correlation metadata.
    /// </summary>
    public required EventEnvelope Envelope { get; init; }

    /// <summary>
    /// Gets the name of the tool that was invoked.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the JSON-serialized arguments passed to the tool.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>
    /// Gets the JSON-serialized result returned by the tool.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool invocation resulted in an error.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// Gets the duration of the tool invocation in milliseconds, if available.
    /// </summary>
    public long? DurationMs { get; init; }
}