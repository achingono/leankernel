namespace LeanKernel.Logic.Policy;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Evaluates all registered <see cref="IPolicy{TEntity}"/> instances.
/// Supports short-circuit evaluation (first deny wins) and full enumeration for audit.
/// </summary>
public sealed class PolicyEvaluator : IPolicyEvaluator
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyEvaluator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider used to resolve typed policies.</param>
    public PolicyEvaluator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public PolicyResult Evaluate<TEntity>(TEntity entity, IPolicyContext context)
        where TEntity : class
    {
        foreach (var policy in ResolvePolicies<TEntity>())
        {
            var result = policy.Evaluate(entity, context);
            if (!result.IsAllowed)
            {
                return result;
            }
        }

        return PolicyResult.Allow();
    }

    /// <inheritdoc />
    public IReadOnlyList<PolicyResult> EvaluateAll<TEntity>(TEntity entity, IPolicyContext context)
        where TEntity : class
    {
        return ResolvePolicies<TEntity>()
            .Select(p => p.Evaluate(entity, context))
            .ToList();
    }

    private IEnumerable<IPolicy<TEntity>> ResolvePolicies<TEntity>()
        where TEntity : class
    {
        var typed = _serviceProvider.GetServices<IPolicy<TEntity>>();

        if (typeof(TEntity) == typeof(object))
        {
            return typed;
        }

        var global = _serviceProvider
            .GetServices<IPolicy<object>>()
            .Select(static policy => new ObjectPolicyAdapter<TEntity>(policy));

        return typed.Concat(global);
    }

    private sealed class ObjectPolicyAdapter<TEntity> : IPolicy<TEntity>
        where TEntity : class
    {
        private readonly IPolicy<object> _inner;

        public ObjectPolicyAdapter(IPolicy<object> inner)
        {
            _inner = inner;
        }

        public string Name => _inner.Name;

        public PolicyResult Evaluate(TEntity entity, IPolicyContext context)
        {
            return _inner.Evaluate(entity, context);
        }
    }
}