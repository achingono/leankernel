namespace LeanKernel.Data.Interceptors;

using LeanKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor for handling auditable entities during save operations in the DbContext.
/// </summary>
public class AuditableInterceptor(IPermit permit) : ISaveChangesInterceptor
{
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
                        entity.CreatedBy = permit.Badge;
                        break;
                    case EntityState.Modified:
                        entity.UpdatedOn = DateTime.UtcNow;
                        entity.UpdatedBy = permit.Badge;
                        break;
                }
            }
        }
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SavingChanges(eventData, result));
    }
}
