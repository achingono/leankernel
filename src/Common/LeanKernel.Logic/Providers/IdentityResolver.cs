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
    /// <inheritdoc />
    public async Task<TenantEntity?> ResolveTenantAsync(string hostName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return null;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.HostName == hostName && t.IsActive, ct);
    }

    /// <inheritdoc />
    public async Task<TenantEntity?> ResolveTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return null;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive, ct);
    }

    /// <inheritdoc />
    public async Task<UserEntity> ResolveOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var (issuer, subject) = ExtractIssuerAndSubject(principal);

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
            if (existing.PersonId == Guid.Empty)
            {
                existing.PersonId = existing.Id;
            }

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
            IsGuest = false,
            PersonId = Guid.Empty
        };

        user.PersonId = user.Id;

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
                if (conflicting.PersonId == Guid.Empty)
                {
                    conflicting.PersonId = conflicting.Id;
                }

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

    /// <inheritdoc />
    public async Task<UserEntity?> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var (issuer, subject) = ExtractIssuerAndSubject(principal);

        if (string.IsNullOrWhiteSpace(subject))
            return null;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject && !u.IsDeleted, ct);

        if (existing is null)
            return null;

        if (existing.PersonId == Guid.Empty)
        {
            existing.PersonId = existing.Id;
        }

        existing.LastActivity = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task<UserEntity> ResolveGuestUserAsync(Guid tenantId, string anonymousUserName, string sessionId, CancellationToken ct = default)
    {
        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        // Embed tenantId in the subject so guests are partitioned by tenant (M5).
        var tenantScopedSubject = $"{tenantId:N}:{sessionId}";

        var guest = await context.Users
            .FirstOrDefaultAsync(u => u.Issuer == "anonymous" && u.Subject == tenantScopedSubject && !u.IsDeleted, ct);

        if (guest is not null)
        {
            if (guest.PersonId == Guid.Empty)
            {
                guest.PersonId = guest.Id;
                await context.SaveChangesAsync(ct);
            }

            return guest;
        }

        guest = new UserEntity
        {
            Id = Guid.NewGuid(),
            Issuer = "anonymous",
            Subject = tenantScopedSubject,
            UserName = anonymousUserName,
            FullName = anonymousUserName,
            IsActive = true,
            IsGuest = true,
            CreatedOn = DateTime.UtcNow,
            PersonId = Guid.Empty
        };

        guest.PersonId = guest.Id;

        context.Users.Add(guest);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            guest = await context.Users
                .FirstOrDefaultAsync(u => u.Issuer == "anonymous" && u.Subject == tenantScopedSubject && !u.IsDeleted, ct);
            if (guest is null)
                throw;

            if (guest.PersonId == Guid.Empty)
            {
                guest.PersonId = guest.Id;
                await context.SaveChangesAsync(ct);
            }
        }

        logger.LogInformation("Created guest user {UserId} (name={Name}, session={SessionId}, tenant={TenantId})", guest!.Id, anonymousUserName, sessionId, tenantId);
        return guest;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<ChannelEntity?> ResolveChannelAsync(string channelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return null;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(channel => channel.Name == channelName, ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsChannelSenderBindingActiveAsync(
        Guid tenantId,
        Guid userId,
        Guid channelId,
        string issuer,
        string subject,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || channelId == Guid.Empty)
            return false;

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return false;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        return await context.ChannelSenderBindings.AnyAsync(binding =>
            binding.IsActive
            && binding.TenantId == tenantId
            && binding.UserId == userId
            && binding.ChannelId == channelId
            && binding.Issuer == issuer
            && binding.Subject == subject,
            ct);
    }

    /// <inheritdoc />
    public async Task<Guid> ResolvePersonIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return Guid.Empty;

        using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null)
            return Guid.Empty;

        if (user.PersonId == Guid.Empty)
        {
            user.PersonId = user.Id;
            await context.SaveChangesAsync(ct);
        }

        return user.PersonId;
    }

    /// <inheritdoc />
    public async Task<Guid> LinkUsersAsync(Guid tenantId, Guid sourceUserId, Guid targetUserId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || sourceUserId == Guid.Empty || targetUserId == Guid.Empty)
            throw new InvalidOperationException("Tenant and user identifiers are required for linking.");

        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var tenantExists = await context.Tenants
            .AnyAsync(t => t.Id == tenantId && t.IsActive, ct);

        if (!tenantExists)
            throw new InvalidOperationException("Cannot link users for an unknown tenant.");

        var source = await context.Users.FirstOrDefaultAsync(u => u.Id == sourceUserId && !u.IsDeleted, ct)
                     ?? throw new InvalidOperationException("Source user does not exist.");
        var target = await context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId && !u.IsDeleted, ct)
                     ?? throw new InvalidOperationException("Target user does not exist.");

        var sourceInTenant = await UserBelongsToTenantAsync(context, source.Id, tenantId, ct);
        var targetInTenant = await UserBelongsToTenantAsync(context, target.Id, tenantId, ct);
        if (!sourceInTenant || !targetInTenant)
            throw new InvalidOperationException("Cannot link users across tenant boundaries.");

        if (source.PersonId == Guid.Empty)
            source.PersonId = source.Id;
        if (target.PersonId == Guid.Empty)
            target.PersonId = target.Id;

        var mergedPersonId = source.PersonId;
        if (target.PersonId != mergedPersonId)
        {
            var targetPersonId = target.PersonId;
            var cluster = await context.Users
                .Where(user => !user.IsDeleted && user.PersonId == targetPersonId)
                .ToListAsync(ct);
            foreach (var member in cluster)
            {
                if (await UserBelongsToTenantAsync(context, member.Id, tenantId, ct))
                {
                    member.PersonId = mergedPersonId;
                }
            }
        }

        await context.SaveChangesAsync(ct);
        return mergedPersonId;
    }

    /// <inheritdoc />
    public async Task UnlinkUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new InvalidOperationException("Tenant and user identifiers are required for unlinking.");

        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var tenantExists = await context.Tenants
            .AnyAsync(t => t.Id == tenantId && t.IsActive, ct);

        if (!tenantExists)
            throw new InvalidOperationException("Cannot unlink users for an unknown tenant.");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
                   ?? throw new InvalidOperationException("User does not exist.");

        var userInTenant = await UserBelongsToTenantAsync(context, user.Id, tenantId, ct);
        if (!userInTenant)
            throw new InvalidOperationException("Cannot unlink users across tenant boundaries.");

        if (user.PersonId == Guid.Empty)
        {
            user.PersonId = user.Id;
        }

        if (user.PersonId == user.Id)
        {
            var samePersonMembers = await context.Users
                .Where(candidate =>
                    !candidate.IsDeleted
                    && candidate.PersonId == user.PersonId
                    && candidate.Id != user.Id)
                .ToListAsync(ct);

            var tenantMembers = new List<UserEntity>(samePersonMembers.Count);
            foreach (var member in samePersonMembers)
            {
                if (await UserBelongsToTenantAsync(context, member.Id, tenantId, ct))
                {
                    tenantMembers.Add(member);
                }
            }

            if (tenantMembers.Count > 0)
            {
                var replacementPersonId = tenantMembers[0].Id;
                foreach (var member in tenantMembers)
                {
                    member.PersonId = replacementPersonId;
                }
            }
        }

        user.PersonId = user.Id;
        await context.SaveChangesAsync(ct);
    }

    private static async Task<bool> UserBelongsToTenantAsync(EntityContext context, Guid userId, Guid tenantId, CancellationToken ct)
    {
        if (await context.Sessions.AnyAsync(session =>
                !session.IsDeleted
                && session.UserId == userId
                && session.TenantId == tenantId,
                ct))
        {
            return true;
        }

        if (await context.ChannelSenderBindings.AnyAsync(binding =>
                binding.UserId == userId
                && binding.TenantId == tenantId,
                ct))
        {
            return true;
        }

        var tenantPrefix = $"{tenantId:N}:";
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId && !candidate.IsDeleted, ct);

        return user is not null
               && string.Equals(user.Issuer, "anonymous", StringComparison.Ordinal)
               && user.Subject.StartsWith(tenantPrefix, StringComparison.Ordinal);
    }

    private static (string Issuer, string Subject) ExtractIssuerAndSubject(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst("lk_sender_iss")?.Value
                     ?? principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value
                     ?? principal.FindFirst("iss")?.Value
                     ?? string.Empty;
        var subject = principal.FindFirst("lk_sender_sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.Sid)?.Value
                      ?? string.Empty;
        return (issuer, subject);
    }
}
