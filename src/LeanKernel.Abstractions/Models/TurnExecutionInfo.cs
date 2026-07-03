namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Describes runtime execution metadata captured for a turn.
/// </summary>
public sealed record TurnExecutionInfo(
    int ToolInvocationCount,
    int SuccessfulToolInvocations,
    TaskStatusDirective? TaskStatus,
    string? ModelUsed);
