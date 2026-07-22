#pragma warning disable SA1649 // File name should match first type name

namespace LeanKernel;

/// <summary>
/// Generic permit for entity-scoped CRUD authorization.
/// </summary>
/// <typeparam name="TEntity">The entity type to authorize against.</typeparam>
public interface IPermit<TEntity> : IPermit
    where TEntity : class
{
    /// <summary>
    /// Determines whether the current identity can perform the specified operation on <typeparamref name="TEntity"/>.
    /// </summary>
    /// <param name="operation">The CRUD operation to authorize.</param>
    /// <returns><c>true</c> if the operation is permitted; otherwise <c>false</c>.</returns>
    bool Can(Operation operation);
}