namespace LeanKernel.Data.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Interceptor for handling auditable entities during save operations in the DbContext.
/// Automatically sets the CreatedOn and UpdatedOn properties for entities implementing the IAuditable interface.
/// </summary>
public class AuditableInterceptor : ISaveChangesInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableInterceptor"/> class.
    /// </summary>
    public AuditableInterceptor()
    {
    }

    /// <summary>
    /// Asynchronously intercepts the save changes operation to handle auditable entities.
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
    /// Intercepts the save changes operation to handle auditable entities.
    /// </summary>
    /// <param name="eventData">The event data related to the save operation.</param>
    /// <param name="result">The interception result.</param>
    /// <returns>The interception result after processing auditable entities.</returns>
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var entries = eventData.Context?.ChangeTracker.Entries() ?? Enumerable.Empty<EntityEntry>();
        var permit = eventData.Context?.GetService<IPermit>();

        ArgumentNullException.ThrowIfNull(permit, nameof(permit));

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
}
