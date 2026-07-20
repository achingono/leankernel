namespace LeanKernel.Data.Interceptors;

using LeanKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor for handling auditable entities during save operations in the DbContext.
/// </summary>
public class AuditableInterceptor : ISaveChangesInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableInterceptor"/> class.
    /// </summary>
    /// <param name="permit">The identity permit for audit badge resolution.</param>
    public AuditableInterceptor(IPermit permit)
    {
        this.permit = permit;
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var entries = eventData.Context?.ChangeTracker.Entries() ?? Enumerable.Empty<EntityEntry>();

        foreach (var entry in entries)
        {
            if (entry.Entity is IAuditable entity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entity.CreatedOn = DateTime.UtcNow;
                        entity.CreatedBy = this.permit.Badge;
                        break;
                    case EntityState.Modified:
                        entity.UpdatedOn = DateTime.UtcNow;
                        entity.UpdatedBy = this.permit.Badge;
                        break;
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(this.SavingChanges(eventData, result));
    }

    private readonly IPermit permit;
}