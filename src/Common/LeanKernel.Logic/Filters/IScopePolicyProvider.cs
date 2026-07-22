namespace LeanKernel.Logic.Filters;

using LeanKernel.Logic.Configuration;

/// <summary>
/// Resolves <see cref="EntityScopePolicy"/> for a given CLR entity type.
/// </summary>
public interface IScopePolicyProvider
{
    /// <summary>
    /// Gets the scope policy for the specified entity type.
    /// </summary>
    /// <param name="entityType">The CLR type of the entity.</param>
    /// <returns>The resolved scope policy.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no policy is configured for the entity type (fail closed).</exception>
    EntityScopePolicy GetPolicy(Type entityType);
}