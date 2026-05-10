namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Authorizes tool invocations before execution.
/// </summary>
public interface IToolExecutionAuthorizer
{
    /// <summary>
    /// Checks whether a tool invocation is allowed.
    /// </summary>
    /// <param name="toolName">The tool name requested by the model.</param>
    /// <param name="parametersJson">The raw JSON parameter payload for the invocation.</param>
    /// <param name="ct">A token used to cancel authorization.</param>
    /// <returns>The tool execution authorization result.</returns>
    Task<ToolExecutionAuthorizationResult> AuthorizeAsync(
        string toolName,
        string parametersJson,
        CancellationToken ct);
}

/// <summary>
/// Result of authorizing a tool invocation.
/// </summary>
public sealed class ToolExecutionAuthorizationResult
{
    /// <summary>
    /// Gets whether tool execution is authorized.
    /// </summary>
    public required bool IsAuthorized { get; init; }

    /// <summary>
    /// Gets the reason for a denied invocation, when applicable.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the semantic action type mapped from the tool invocation.
    /// </summary>
    public string? ActionType { get; init; }

    /// <summary>
    /// Creates an allowed authorization result.
    /// </summary>
    /// <param name="actionType">The semantic action type, if one was mapped.</param>
    /// <returns>An allowed authorization result.</returns>
    public static ToolExecutionAuthorizationResult Allow(string? actionType = null) =>
        new() { IsAuthorized = true, ActionType = actionType };

    /// <summary>
    /// Creates a denied authorization result.
    /// </summary>
    /// <param name="reason">The denial reason.</param>
    /// <param name="actionType">The semantic action type, if one was mapped.</param>
    /// <returns>A denied authorization result.</returns>
    public static ToolExecutionAuthorizationResult Deny(string reason, string? actionType = null) =>
        new() { IsAuthorized = false, Reason = reason, ActionType = actionType };
}
