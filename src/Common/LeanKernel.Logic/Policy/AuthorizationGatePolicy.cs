namespace LeanKernel.Logic.Policy;

/// <summary>
/// Wraps the existing <see cref="IPermit{TEntity}.Can(Operation)"/> check as a domain policy.
/// This bridges the Phase 19 enforcement model into the policy core without creating
/// a parallel authorization path.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AuthorizationGatePolicy<TEntity> : IPolicy<TEntity>
    where TEntity : class
{
    private readonly IPermit<TEntity> _permit;
    private readonly Operation _operation;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationGatePolicy{TEntity}"/> class.
    /// </summary>
    /// <param name="permit">The entity-scoped permit.</param>
    /// <param name="operation">The operation to authorize.</param>
    public AuthorizationGatePolicy(IPermit<TEntity> permit, Operation operation)
    {
        _permit = permit;
        _operation = operation;
    }

    /// <inheritdoc />
    public string Name => $"AuthorizationGate:{typeof(TEntity).Name}:{_operation}";

    /// <inheritdoc />
    public PolicyResult Evaluate(TEntity entity, IPolicyContext context)
    {
        return _permit.Can(_operation)
            ? PolicyResult.Allow()
            : PolicyResult.Deny($"Operation {_operation} is not permitted for {typeof(TEntity).Name}.");
    }
}