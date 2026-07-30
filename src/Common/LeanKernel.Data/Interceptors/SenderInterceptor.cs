namespace LeanKernel.Data.Interceptors;

using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
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
        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ChannelSenderBindingEntity entity
                && entry.State is EntityState.Added or EntityState.Modified
                && string.IsNullOrWhiteSpace(entity.BearerToken))
            {
                context.Entry(entity).Reference(e => e.User).Load();
                context.Entry(entity).Reference(e => e.Tenant).Load();
                context.Entry(entity).Reference(e => e.Channel).Load();
                entity.BearerToken = this.securityTokenGenerator.GenerateToken(entity, true);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ChannelSenderBindingEntity entity
                && entry.State is EntityState.Added or EntityState.Modified
                && string.IsNullOrWhiteSpace(entity.BearerToken))
            {
                await context.Entry(entity).Reference(e => e.User).LoadAsync(cancellationToken);
                await context.Entry(entity).Reference(e => e.Tenant).LoadAsync(cancellationToken);
                await context.Entry(entity).Reference(e => e.Channel).LoadAsync(cancellationToken);
                entity.BearerToken = this.securityTokenGenerator.GenerateToken(entity, true);
            }
        }

        return result;
    }

    private readonly ISecurityTokenGenerator securityTokenGenerator;
}