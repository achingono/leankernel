namespace LeanKernel.Data.Interceptors;

using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Interceptor for handling recyclable entities during save operations in the DbContext.
/// Automatically marks entities implementing the IRecyclable interface as deleted instead of physically removing them.
/// </summary>
public class RecyclableInterceptor : ISaveChangesInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecyclableInterceptor"/> class.
    /// </summary>
    public RecyclableInterceptor()
    {
    }

    /// <summary>
    /// Asynchronously intercepts the save changes operation to handle recyclable entities.
    /// </summary>
    /// <param name="eventData">The event data related to the save operation.</param>
    /// <param name="result">The interception result.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        return await ValueTask.FromResult(SavingChanges(eventData, result));
    }

    /// <summary>
    /// Intercepts the save changes operation to handle recyclable entities.
    /// </summary>
    /// <param name="eventData">The event data related to the save operation.</param>
    /// <param name="result">The interception result.</param>
    /// <returns>The interception result after processing recyclable entities.</returns>
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var entries = eventData.Context?.ChangeTracker.Entries() ?? Enumerable.Empty<EntityEntry>();
        var permit = eventData.Context?.GetService<IPermit>()!;

        ArgumentNullException.ThrowIfNull(permit, nameof(permit));

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
}
