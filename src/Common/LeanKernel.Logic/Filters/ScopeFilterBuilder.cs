namespace LeanKernel.Logic.Filters;

using System.Linq.Expressions;

using LeanKernel.Logic.Configuration;

/// <summary>
/// Builds EF-translatable <see cref="Expression{TDelegate}"/> predicates from scope policy
/// and request permit values. Supports direct and navigation-based scope property resolution.
/// </summary>
public sealed class ScopeFilterBuilder
{
    /// <summary>
    /// Builds a predicate expression that constrains <typeparamref name="TEntity"/>
    /// to the scope dimensions defined in <paramref name="policy"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to filter.</typeparam>
    /// <param name="policy">The scope policy defining which dimensions to enforce.</param>
    /// <param name="permit">The request identity providing the dimension values.</param>
    /// <returns>An expression predicate suitable for EF Core <c>Where</c> clauses.</returns>
    public Expression<Func<TEntity, bool>> Build<TEntity>(EntityScopePolicy policy, IPermit permit)
        where TEntity : class
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var target = policy.NavigationPath is not null
            ? Navigate(param, policy.NavigationPath)
            : (Expression)param;

        var conditions = new List<Expression>();

        AddEqualityCondition(conditions, target, "TenantId", permit.TenantId, policy.Dimensions, ScopeDimension.Tenant);
        AddEqualityCondition(conditions, target, "UserId", permit.UserId, policy.Dimensions, ScopeDimension.User);
        AddEqualityCondition(conditions, target, "ChannelId", permit.ChannelId, policy.Dimensions, ScopeDimension.Channel);

        var body = conditions.Aggregate(Expression.AndAlso);
        return Expression.Lambda<Func<TEntity, bool>>(body, param);
    }

    private static void AddEqualityCondition(
        List<Expression> conditions,
        Expression target,
        string propertyName,
        Guid value,
        ScopeDimension activeDimensions,
        ScopeDimension flag)
    {
        if (!activeDimensions.HasFlag(flag))
        {
            return;
        }

        var property = Expression.Property(target, propertyName);
        conditions.Add(Expression.Equal(property, Expression.Constant(value)));
    }

    private static Expression Navigate(Expression source, string path)
    {
        foreach (var segment in path.Split('.'))
        {
            source = Expression.Property(source, segment);
        }

        return source;
    }
}