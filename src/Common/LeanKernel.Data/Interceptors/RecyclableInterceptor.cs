namespace LeanKernel.Data.Interceptors;

using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using LeanKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor for handling recyclable entities during save operations in the DbContext.
/// </summary>
public class RecyclableInterceptor(IPermit permit) : ISaveChangesInterceptor
{
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var entries = eventData.Context?.ChangeTracker.Entries() ?? Enumerable.Empty<EntityEntry>();

        foreach (var entry in entries.Where(e => e.State == EntityState.Deleted))
        {
            if (entry.Entity is IRecyclable recyclable)
            {
                recyclable.IsDeleted = true;
                entry.State = EntityState.Modified;

                if (entry.Entity is IAuditable auditable)
                {
                    auditable.UpdatedOn = DateTime.UtcNow;
                    auditable.UpdatedBy = permit.Badge;
                }
            }

            if (entry.Entity.GetType().GetCustomAttributes<ComplexTypeAttribute>().Any())
            {
                entry.State = EntityState.Unchanged;
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
