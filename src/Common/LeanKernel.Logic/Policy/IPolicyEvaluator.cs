namespace LeanKernel.Logic.Policy;

/// <summary>
/// Evaluates all registered policies for a given entity and context.
/// Supports short-circuit (first deny) and full-evaluation modes.
/// </summary>
public interface IPolicyEvaluator
{
    /// <summary>
    /// Evaluates policies in registration order and returns the first denial,
    /// or <see cref="PolicyResult.Allow"/> if all policies pass.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to evaluate.</param>
    /// <param name="context">The policy evaluation context.</param>
    /// <returns>The first deny result, or allow.</returns>
    PolicyResult Evaluate<TEntity>(TEntity entity, IPolicyContext context)
        where TEntity : class;

    /// <summary>
    /// Evaluates all policies and returns every result for audit/aggregation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to evaluate.</param>
    /// <param name="context">The policy evaluation context.</param>
    /// <returns>A list of all policy results.</returns>
    IReadOnlyList<PolicyResult> EvaluateAll<TEntity>(TEntity entity, IPolicyContext context)
        where TEntity : class;
}