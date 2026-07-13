using System.Security.Claims;
using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Resolves persisted tenant, user, and channel entities from request inputs using EF Core.
/// </summary>
public sealed class IdentityResolver(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<IdentityResolver> logger) : IIdentityResolver
{
    public async Task<TenantEntity?> ResolveTenantAsync(string hostName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return null;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.HostName == hostName && t.IsActive, ct);
    }

    public async Task<UserEntity> ResolveOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var issuer = principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value
                     ?? principal.FindFirst("iss")?.Value
                     ?? string.Empty;
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.Sid)?.Value
                      ?? string.Empty;

        if (string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("Cannot resolve user: no subject claim found in principal.");
            throw new InvalidOperationException("Cannot resolve user: no subject claim found.");
        }

        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject && !u.IsDeleted, ct);

        if (existing is not null)
        {
            existing.LastActivity = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
            return existing;
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Issuer = issuer,
            Subject = subject,
            UserName = principal.FindFirst(ClaimTypes.Name)?.Value ?? subject,
            Email = principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal.FindFirst("email")?.Value
                    ?? string.Empty,
            FirstName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty,
            LastName = principal.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty,
            FullName = principal.FindFirst(ClaimTypes.Name)?.Value ?? subject,
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            IsGuest = false
        };

        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var conflicting = await context.Users
                .FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject && !u.IsDeleted, ct);
            if (conflicting is not null)
            {
                conflicting.LastActivity = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Created new user {UserId} for issuer={Issuer}, subject={Subject}",
                    conflicting.Id, issuer, subject);
                return conflicting;
            }
            throw;
        }

        logger.LogInformation("Created new user {UserId} for issuer={Issuer}, subject={Subject}",
            user.Id, issuer, subject);
        return user;
    }

    public async Task<UserEntity> ResolveGuestUserAsync(Guid tenantId, string anonymousUserName, string sessionId, CancellationToken ct = default)
    {
        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var guest = await context.Users
            .FirstOrDefaultAsync(u => u.Issuer == "anonymous" && u.Subject == sessionId && !u.IsDeleted, ct);

        if (guest is not null)
            return guest;

        guest = new UserEntity
        {
            Id = Guid.NewGuid(),
            Issuer = "anonymous",
            Subject = sessionId,
            UserName = anonymousUserName,
            FullName = anonymousUserName,
            IsActive = true,
            IsGuest = true,
            CreatedOn = DateTime.UtcNow
        };

        context.Users.Add(guest);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            guest = await context.Users
                .FirstOrDefaultAsync(u => u.Issuer == "anonymous" && u.Subject == sessionId && !u.IsDeleted, ct);
            if (guest is null)
                throw;
        }

        logger.LogInformation("Created guest user {UserId} (name={Name}, session={SessionId})", guest!.Id, anonymousUserName, sessionId);
        return guest;
    }

    public async Task<ChannelEntity> ResolveOrCreateChannelAsync(string channelName, CancellationToken ct = default)
    {
        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var channel = await context.Channels
            .FirstOrDefaultAsync(c => c.Name == channelName, ct);

        if (channel is not null)
            return channel;

        channel = new ChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = channelName
        };

        context.Channels.Add(channel);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var existing = await context.Channels
                .FirstOrDefaultAsync(c => c.Name == channelName, ct);
            if (existing is not null)
            {
                logger.LogInformation("Created channel {ChannelId} (name={Name})", existing.Id, channelName);
                return existing;
            }
            throw;
        }

        logger.LogInformation("Created channel {ChannelId} (name={Name})", channel.Id, channelName);
        return channel;
    }
}
