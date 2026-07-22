namespace LeanKernel.Logic.Filters;

using System.Linq.Expressions;

using LeanKernel.Logic.Interfaces;

/// <summary>
/// Open-generic <see cref="IFilter{TEntity}"/> that applies scope and soft-delete predicates
/// based on the current request identity's permit. Fails closed when read is not permitted.
/// </summary>
/// <typeparam name="TEntity">The entity type to filter.</typeparam>
public sealed class ScopeDrivenFilter<TEntity> : IFilter<TEntity>
    where TEntity : class
{
    private readonly IPermit<TEntity> _permit;
    private readonly IScopePolicyProvider _policyProvider;
    private readonly ScopeFilterBuilder _filterBuilder;
    private static readonly Expression<Func<TEntity, bool>>? _softDeletePredicate = BuildSoftDeletePredicate();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeDrivenFilter{TEntity}"/> class.
    /// </summary>
    /// <param name="permit">The request-scoped entity permit.</param>
    /// <param name="policyProvider">The scope policy provider.</param>
    /// <param name="filterBuilder">The expression builder for scope predicates.</param>
    public ScopeDrivenFilter(
        IPermit<TEntity> permit,
        IScopePolicyProvider policyProvider,
        ScopeFilterBuilder filterBuilder)
    {
        _permit = permit;
        _policyProvider = policyProvider;
        _filterBuilder = filterBuilder;
    }

    /// <inheritdoc />
    public Expression<Func<TEntity, bool>>? Predicate
    {
        get
        {
            if (!_permit.Can(Operation.Read))
            {
                return _ => false;
            }

            var policy = _policyProvider.GetPolicy(typeof(TEntity));
            var scopePredicate = _filterBuilder.Build<TEntity>(policy, _permit);
            return Combine(scopePredicate, _softDeletePredicate);
        }
    }

    private static Expression<Func<TEntity, bool>>? BuildSoftDeletePredicate()
    {
        if (!typeof(IRecyclable).IsAssignableFrom(typeof(TEntity)))
        {
            return null;
        }

        var param = Expression.Parameter(typeof(TEntity), "e");
        var isDeleted = Expression.Property(param, nameof(IRecyclable.IsDeleted));
        var body = Expression.Equal(isDeleted, Expression.Constant(false));
        return Expression.Lambda<Func<TEntity, bool>>(body, param);
    }

    private static Expression<Func<TEntity, bool>>? Combine(
        Expression<Func<TEntity, bool>>? first,
        Expression<Func<TEntity, bool>>? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var parameter = first.Parameters[0];
        var replacer = new ParameterReplacer(second.Parameters[0], parameter);
        var secondBody = replacer.Visit(second.Body);
        var body = Expression.AndAlso(first.Body, secondBody);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _from ? _to : base.VisitParameter(node);
        }
    }
}