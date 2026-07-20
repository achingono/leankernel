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
public class RecyclableInterceptor : ISaveChangesInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecyclableInterceptor"/> class.
    /// </summary>
    /// <param name="permit">The identity permit for audit badge resolution.</param>
    public RecyclableInterceptor(IPermit permit)
    {
        this.permit = permit;
    }

    /// <inheritdoc />
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
                    auditable.UpdatedBy = this.permit.Badge;
                }
            }

            if (entry.Entity.GetType().GetCustomAttributes<ComplexTypeAttribute>().Any())
            {
                entry.State = EntityState.Unchanged;
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