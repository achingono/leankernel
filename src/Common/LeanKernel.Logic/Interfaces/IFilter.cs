namespace LeanKernel.Logic.Interfaces;

using System.Linq.Expressions;

/// <summary>
/// Provides a query predicate that constrains access to <typeparamref name="TEntity"/>
/// based on the current request identity and scope policy.
/// </summary>
/// <typeparam name="TEntity">The entity type to filter.</typeparam>
public interface IFilter<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Gets the predicate expression to apply to queries for <typeparamref name="TEntity"/>.
    /// Returns <c>null</c> when no additional filtering is required (e.g. admin read-all).
    /// </summary>
    Expression<Func<TEntity, bool>>? Predicate { get; }
}