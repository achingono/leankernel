namespace LeanKernel.Logic.Repositories;

using System.Collections.Concurrent;
using System.Reflection;

using LeanKernel.Data;
using LeanKernel.Logic.Interfaces;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core-backed repository for Guid-key entities.
/// Applies <see cref="IFilter{TEntity}"/> predicates on all reads and
/// enforces <see cref="IPermit{TEntity}"/> authorization on all writes.
/// Uses a scoped <see cref="EntityContext"/> to ensure safe <c>IQueryable</c> lifetime.
/// </summary>
/// <typeparam name="TEntity">The entity type, constrained to <see cref="IEntity"/>.</typeparam>
public sealed class EntityRepository<TEntity> : IRepository<TEntity>
    where TEntity : class, IEntity
{
    private static readonly ConcurrentDictionary<Type, PartitionKeyProps> PartitionKeyCache = new();

    private readonly EntityContext _context;
    private readonly IFilter<TEntity> _filter;
    private readonly IPermit<TEntity> _permit;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityRepository{TEntity}"/> class.
    /// </summary>
    /// <param name="context">The scoped EF Core context.</param>
    /// <param name="filter">The scope-driven filter for this entity type.</param>
    /// <param name="permit">The request-scoped entity permit.</param>
    public EntityRepository(
        EntityContext context,
        IFilter<TEntity> filter,
        IPermit<TEntity> permit)
    {
        _context = context;
        _filter = filter;
        _permit = permit;
    }

    /// <inheritdoc />
    public IQueryable<TEntity> GetAll()
    {
        var query = _context.Set<TEntity>().AsQueryable();

        if (_filter.Predicate is not null)
        {
            query = query.Where(_filter.Predicate);
        }

        return query;
    }

    /// <inheritdoc />
    public async ValueTask<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetAll().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(TEntity entity)
    {
        if (!_permit.Can(Operation.Create))
        {
            throw new InvalidOperationException(
                $"Create not permitted for {typeof(TEntity).Name}.");
        }

        StampAuditableFields(entity);
        StampPartitionKeys(entity);
        _context.Set<TEntity>().Add(entity);
    }

    /// <inheritdoc />
    public void Update(TEntity entity)
    {
        if (!_permit.Can(Operation.Update))
        {
            throw new InvalidOperationException(
                $"Update not permitted for {typeof(TEntity).Name}.");
        }

        StampAuditableFields(entity);
        _context.Set<TEntity>().Update(entity);
    }

    /// <inheritdoc />
    public void Delete(TEntity entity)
    {
        if (!_permit.Can(Operation.Delete))
        {
            throw new InvalidOperationException(
                $"Delete not permitted for {typeof(TEntity).Name}.");
        }

        if (!GetAll().Any(e => e.Id == entity.Id))
        {
            throw new InvalidOperationException(
                $"Entity not found or access denied for delete of {typeof(TEntity).Name}.");
        }

        _context.Set<TEntity>().Remove(entity);
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    private void StampAuditableFields(TEntity entity)
    {
        if (entity is IAuditable auditable)
        {
            auditable.CreatedOn = auditable.CreatedOn == default
                ? DateTime.UtcNow
                : auditable.CreatedOn;

            auditable.CreatedBy = auditable.CreatedBy == null || auditable.CreatedBy.Id == Guid.Empty
                ? _permit.Badge
                : auditable.CreatedBy;

            auditable.UpdatedOn = DateTime.UtcNow;
            auditable.UpdatedBy = _permit.Badge;
        }
    }

    private void StampPartitionKeys(TEntity entity)
    {
        var props = PartitionKeyCache.GetOrAdd(typeof(TEntity), static t => new PartitionKeyProps
        {
            TenantId = t.GetProperty("TenantId"),
            UserId = t.GetProperty("UserId"),
            ChannelId = t.GetProperty("ChannelId"),
        });

        TrySetValue(entity, props.TenantId, _permit.TenantId);
        TrySetValue(entity, props.UserId, _permit.UserId);
        TrySetValue(entity, props.ChannelId, _permit.ChannelId);
    }

    private static void TrySetValue(TEntity entity, PropertyInfo? prop, Guid value)
    {
        if (prop?.CanWrite == true && prop.PropertyType == typeof(Guid))
        {
            var current = (Guid)prop.GetValue(entity)!;
            if (current == Guid.Empty)
            {
                prop.SetValue(entity, value);
            }
        }
    }

    private sealed class PartitionKeyProps
    {
        public PropertyInfo? TenantId { get; init; }

        public PropertyInfo? UserId { get; init; }

        public PropertyInfo? ChannelId { get; init; }
    }
}