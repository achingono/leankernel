namespace LeanKernel.Logic.Interfaces;

/// <summary>
/// Generic repository contract for Guid-key entities that enforces
/// <see cref="IFilter{TEntity}"/> predicates on reads and <see cref="IPermit{TEntity}"/>
/// authorization on writes.
/// </summary>
/// <typeparam name="TEntity">The entity type, constrained to <see cref="IEntity"/>.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class, IEntity
{
    /// <summary>
    /// Returns a filtered queryable for <typeparamref name="TEntity"/>
    /// with the current scope and soft-delete predicates applied.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> with filters applied.</returns>
    IQueryable<TEntity> GetAll();

    /// <summary>
    /// Finds an entity by its Guid identifier, returning <c>null</c> if not found or out of scope.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity if found and within scope; otherwise <c>null</c>.</returns>
    ValueTask<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the entity for insert after verifying <see cref="Operation.Create"/> is permitted.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when create is not permitted.</exception>
    void Add(TEntity entity);

    /// <summary>
    /// Marks the entity for update after verifying <see cref="Operation.Update"/> is permitted.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <exception cref="InvalidOperationException">Thrown when update is not permitted.</exception>
    void Update(TEntity entity);

    /// <summary>
    /// Marks the entity for deletion after verifying <see cref="Operation.Delete"/> is permitted.
    /// </summary>
    /// <param name="entity">The entity to delete.</param>
    /// <exception cref="InvalidOperationException">Thrown when delete is not permitted.</exception>
    void Delete(TEntity entity);

    /// <summary>
    /// Saves all pending changes to the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}