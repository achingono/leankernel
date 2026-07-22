namespace LeanKernel.Logic.Policy;

/// <summary>
/// A domain policy that evaluates a business rule against an entity and context.
/// Policies are evaluated <em>above</em> the repository layer and compose with
/// <c>IPermit{TEntity}</c>, <c>IFilter{TEntity}</c>,
/// and <c>IRepository{TEntity}</c> rather than replacing them.
/// </summary>
/// <typeparam name="TEntity">The entity type this policy evaluates.</typeparam>
public interface IPolicy<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Gets the policy name for identification and auditing.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates the policy against the given entity and context.
    /// </summary>
    /// <param name="entity">The entity to evaluate.</param>
    /// <param name="context">The policy evaluation context.</param>
    /// <returns>A policy result indicating allow or deny.</returns>
    PolicyResult Evaluate(TEntity entity, IPolicyContext context);
}