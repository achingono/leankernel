namespace LeanKernel.Logic.Policy;

/// <summary>
/// Provides the context for policy evaluation, including the canonical identity
/// and the request-scoped permit. Policies use this context to make domain-level
/// decisions that compose with (rather than replace) the repository enforcement path.
/// </summary>
public interface IPolicyContext
{
    /// <summary>
    /// Gets the canonical identity context for the current request.
    /// </summary>
    IdentityContext Identity { get; }

    /// <summary>
    /// Gets the request-scoped permit for CRUD-level authorization checks.
    /// </summary>
    IPermit Permit { get; }

    /// <summary>
    /// Gets additional metadata for policy evaluation.
    /// </summary>
    IReadOnlyDictionary<string, object?> Metadata { get; }
}