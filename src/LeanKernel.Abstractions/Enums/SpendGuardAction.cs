namespace LeanKernel.Abstractions.Enums;

/// <summary>
/// Represents a spend-guard action.
/// </summary>
public enum SpendGuardAction
{
    /// <summary>
    /// Allow the request.
    /// </summary>
    Allow,

    /// <summary>
    /// Allow the request and emit a warning.
    /// </summary>
    Warn,

    /// <summary>
    /// Block the request.
    /// </summary>
    Block,
}
