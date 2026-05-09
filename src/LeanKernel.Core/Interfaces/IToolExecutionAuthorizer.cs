namespace LeanKernel.Core.Interfaces;

public interface IToolExecutionAuthorizer
{
    Task<ToolExecutionAuthorizationResult> AuthorizeAsync(
        string toolName,
        string parametersJson,
        CancellationToken ct);
}

public sealed class ToolExecutionAuthorizationResult
{
    public required bool IsAuthorized { get; init; }
    public string? Reason { get; init; }
    public string? ActionType { get; init; }

    public static ToolExecutionAuthorizationResult Allow(string? actionType = null) =>
        new() { IsAuthorized = true, ActionType = actionType };

    public static ToolExecutionAuthorizationResult Deny(string reason, string? actionType = null) =>
        new() { IsAuthorized = false, Reason = reason, ActionType = actionType };
}