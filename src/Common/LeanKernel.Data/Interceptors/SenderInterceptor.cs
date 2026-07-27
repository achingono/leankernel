namespace LeanKernel.Data.Interceptors;

using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor for handling sender entities during save operations in the DbContext.
/// </summary>
public class SenderInterceptor : ISaveChangesInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SenderInterceptor"/> class.
    /// </summary>
    /// <param name="securityTokenGenerator">The security token generator for generating bearer tokens.</param>
    public SenderInterceptor(ISecurityTokenGenerator securityTokenGenerator)
    {
        this.securityTokenGenerator = securityTokenGenerator;
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var entries = eventData.Context?.ChangeTracker.Entries() ?? Enumerable.Empty<EntityEntry>();

        foreach (var entry in entries)
        {
            if (entry.Entity is ChannelSenderBindingEntity entity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                    case EntityState.Modified:
                        if (string.IsNullOrWhiteSpace(entity.BearerToken))
                        {
                            entity.BearerToken = this.securityTokenGenerator.GenerateToken(entity, false);
                        }

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

    private readonly ISecurityTokenGenerator securityTokenGenerator;
}