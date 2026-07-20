using System.Security.Claims;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Covers tenant, user, and channel resolution behavior.
/// </summary>
public class IdentityResolverTests
{
    /// <summary>
    /// Verifies blank host names do not resolve a tenant.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveTenantAsync_BlankHost_ReturnsNull()
    {
        var resolver = CreateResolver(out _);

        var tenant = await resolver.ResolveTenantAsync(" ");

        tenant.Should().BeNull();
    }

    /// <summary>
    /// Verifies only active tenants are returned for a host.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveTenantAsync_ReturnsOnlyActiveTenant()
    {
        var resolver = CreateResolver(out var db);
        db.Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "A",
            HostName = "a.test",
            IsActive = false,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "B",
            HostName = "b.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        await db.SaveChangesAsync();

        (await resolver.ResolveTenantAsync("a.test")).Should().BeNull();
        (await resolver.ResolveTenantAsync("b.test"))!.HostName.Should().Be("b.test");
    }

    /// <summary>
    /// Verifies users are created once and then reused.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveOrCreateUserAsync_CreatesAndFindsUser()
    {
        var resolver = CreateResolver(out var db);
        var principal = Principal("sub-1", "issuer-1", "Jane", "jane@test");

        var created = await resolver.ResolveOrCreateUserAsync(principal);
        created.Subject.Should().Be("sub-1");
        created.PersonId.Should().Be(created.Id);

        var fetched = await resolver.ResolveOrCreateUserAsync(principal);
        fetched.Id.Should().Be(created.Id);
        fetched.LastActivity.Should().NotBeNull();
        db.Users.Count().Should().Be(1);
    }

    /// <summary>
    /// Verifies missing subject claims are rejected.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveOrCreateUserAsync_WithoutSubject_Throws()
    {
        var resolver = CreateResolver(out _);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));

        var act = () => resolver.ResolveOrCreateUserAsync(principal);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies guest users and channels are created once and then reused.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveGuestAndChannel_CreateThenReuse()
    {
        var resolver = CreateResolver(out var db);

        var tenantId = Guid.NewGuid();
        var guest1 = await resolver.ResolveGuestUserAsync(tenantId, "anonymous", "session-1");
        var guest2 = await resolver.ResolveGuestUserAsync(tenantId, "anonymous", "session-1");
        guest2.Id.Should().Be(guest1.Id);
        guest2.PersonId.Should().Be(guest1.PersonId);

        var channel1 = await resolver.ResolveOrCreateChannelAsync("openai-http");
        var channel2 = await resolver.ResolveOrCreateChannelAsync("openai-http");
        channel2.Id.Should().Be(channel1.Id);

        db.Users.Count().Should().Be(1);
        db.Channels.Count().Should().Be(1);
    }

    [Fact]
    public async Task ResolveUserAsync_WhenMissing_ReturnsNull()
    {
        var resolver = CreateResolver(out _);

        var principal = Principal("missing-user", "signal", "Missing", "missing@test");

        var existing = await resolver.ResolveUserAsync(principal);

        existing.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserAsync_WhenExisting_ReturnsUser()
    {
        var resolver = CreateResolver(out _);

        var principal = Principal("existing-sub", "signal", "Existing", "existing@test");
        var created = await resolver.ResolveOrCreateUserAsync(principal);

        var existing = await resolver.ResolveUserAsync(principal);

        existing.Should().NotBeNull();
        existing!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task IsChannelSenderBindingActiveAsync_RequiresExactMatch()
    {
        var resolver = CreateResolver(out var db);
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var principal = Principal("+15551234", "signal", "Signal User", "signal@test");
        var user = await resolver.ResolveOrCreateUserAsync(principal);

        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = "Tenant",
            HostName = "tenant.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Channels.Add(new ChannelEntity { Id = channelId, Name = ChannelEntity.SignalName });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            UserId = user.Id,
            Issuer = "signal",
            Subject = "+15551234",
            BearerToken = "test-token",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var active = await resolver.IsChannelSenderBindingActiveAsync(tenantId, user.Id, channelId, "signal", "+15551234");
        var wrongSubject = await resolver.IsChannelSenderBindingActiveAsync(tenantId, user.Id, channelId, "signal", "+15557654");

        active.Should().BeTrue();
        wrongSubject.Should().BeFalse();
    }

    /// <summary>
    /// M5: Two tenants sharing the same anonymous session ID must resolve to different guest users.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task ResolveGuestUserAsync_DifferentTenants_SameSession_CreatesDifferentUsers()
    {
        var resolver = CreateResolver(out var db);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string sessionId = "shared-anon-session";

        var guestA = await resolver.ResolveGuestUserAsync(tenantA, "anonymous", sessionId);
        var guestB = await resolver.ResolveGuestUserAsync(tenantB, "anonymous", sessionId);

        guestA.Id.Should().NotBe(guestB.Id,
            because: "guest identity must be tenant-scoped to prevent cross-tenant isolation break");
        guestA.Subject.Should().Contain(tenantA.ToString("N"),
            because: "subject must embed tenantId so the same session ID is unique across tenants");
        guestB.Subject.Should().Contain(tenantB.ToString("N"));
        db.Users.Count().Should().Be(2);
    }

    [Fact]
    public async Task LinkAndUnlinkUsersAsync_UpdatesPersonMembership()
    {
        var resolver = CreateResolver(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = "Tenant",
            HostName = "tenant-link.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        await db.SaveChangesAsync();

        var userA = await resolver.ResolveOrCreateUserAsync(Principal("a", "iss-a", "A", "a@test"));
        var userB = await resolver.ResolveOrCreateUserAsync(Principal("b", "iss-b", "B", "b@test"));

        var channelId = Guid.NewGuid();
        db.Channels.Add(new ChannelEntity { Id = channelId, Name = "unit-test-channel" });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userA.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userB.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            UserId = userA.Id,
            Issuer = "iss-a",
            Subject = "a",
            IsActive = true
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            UserId = userB.Id,
            Issuer = "iss-b",
            Subject = "b",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var linkedPersonId = await resolver.LinkUsersAsync(tenantId, userA.Id, userB.Id);
        linkedPersonId.Should().Be(userA.PersonId);

        var resolvedPersonB = await resolver.ResolvePersonIdAsync(userB.Id);
        resolvedPersonB.Should().Be(linkedPersonId);

        await resolver.UnlinkUserAsync(tenantId, userB.Id);
        var unlinkedPersonB = await resolver.ResolvePersonIdAsync(userB.Id);
        unlinkedPersonB.Should().Be(userB.Id);
    }

    [Fact]
    public async Task LinkUsersAsync_WhenUserOutsideTenant_Throws()
    {
        var resolver = CreateResolver(out var db);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantA,
            Name = "Tenant A",
            HostName = "a-link.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantB,
            Name = "Tenant B",
            HostName = "b-link.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });

        var channelId = Guid.NewGuid();
        db.Channels.Add(new ChannelEntity { Id = channelId, Name = "link-test-channel" });
        await db.SaveChangesAsync();

        var userA = await resolver.ResolveOrCreateUserAsync(Principal("ta", "iss-ta", "TA", "ta@test"));
        var userB = await resolver.ResolveOrCreateUserAsync(Principal("tb", "iss-tb", "TB", "tb@test"));

        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            UserId = userA.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            UserId = userB.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ChannelId = channelId,
            UserId = userA.Id,
            Issuer = "iss-ta",
            Subject = "ta",
            IsActive = true
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            ChannelId = channelId,
            UserId = userB.Id,
            Issuer = "iss-tb",
            Subject = "tb",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var act = () => resolver.LinkUsersAsync(tenantA, userA.Id, userB.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UnlinkUserAsync_WhenAnchorUser_ReassignsTenantMembersAndIsIdempotent()
    {
        var resolver = CreateResolver(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = "Tenant",
            HostName = "anchor-unlink.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });

        var channelId = Guid.NewGuid();
        db.Channels.Add(new ChannelEntity { Id = channelId, Name = "anchor-channel" });
        await db.SaveChangesAsync();

        var userA = await resolver.ResolveOrCreateUserAsync(Principal("anchor-a", "iss-anchor", "A", "a@anchor.test"));
        var userB = await resolver.ResolveOrCreateUserAsync(Principal("anchor-b", "iss-anchor", "B", "b@anchor.test"));

        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userA.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userB.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            UserId = userA.Id,
            Issuer = "iss-anchor",
            Subject = "anchor-a",
            IsActive = true
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channelId,
            UserId = userB.Id,
            Issuer = "iss-anchor",
            Subject = "anchor-b",
            IsActive = true
        });
        await db.SaveChangesAsync();

        await resolver.LinkUsersAsync(tenantId, userA.Id, userB.Id);

        await resolver.UnlinkUserAsync(tenantId, userA.Id);
        await resolver.UnlinkUserAsync(tenantId, userA.Id);

        var personA = await resolver.ResolvePersonIdAsync(userA.Id);
        var personB = await resolver.ResolvePersonIdAsync(userB.Id);

        personA.Should().Be(userA.Id);
        personB.Should().NotBe(userA.Id);
    }

    [Fact]
    public async Task LinkUsersAsync_DoesNotMutateCrossTenantClusterMembers()
    {
        var resolver = CreateResolver(out var db);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Tenants.Add(new TenantEntity
        {
            Id = tenantA,
            Name = "Tenant A",
            HostName = "cluster-a.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantB,
            Name = "Tenant B",
            HostName = "cluster-b.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });

        var channelId = Guid.NewGuid();
        db.Channels.Add(new ChannelEntity { Id = channelId, Name = "cluster-channel" });
        await db.SaveChangesAsync();

        var sourceA = new UserEntity
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.Empty,
            Issuer = "iss-source",
            Subject = "source-a",
            UserName = "source-a",
            FullName = "Source",
            Email = "source@a.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        };
        sourceA.PersonId = sourceA.Id;

        var targetA = new UserEntity
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.Empty,
            Issuer = "iss-target",
            Subject = "target-a",
            UserName = "target-a",
            FullName = "Target",
            Email = "target@a.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        };
        targetA.PersonId = targetA.Id;

        var tenantBUser = new UserEntity
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.Empty,
            Issuer = "iss-other",
            Subject = "other-b",
            UserName = "other-b",
            FullName = "Other",
            Email = "other@b.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        };
        tenantBUser.PersonId = targetA.PersonId;

        db.Users.Add(sourceA);
        db.Users.Add(targetA);
        db.Users.Add(tenantBUser);
        await db.SaveChangesAsync();

        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            UserId = sourceA.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            UserId = targetA.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            UserId = tenantBUser.Id,
            ChannelId = channelId,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ChannelId = channelId,
            UserId = sourceA.Id,
            Issuer = "iss-source",
            Subject = "source-a",
            IsActive = true
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ChannelId = channelId,
            UserId = targetA.Id,
            Issuer = "iss-target",
            Subject = "target-a",
            IsActive = true
        });
        db.ChannelSenderBindings.Add(new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            ChannelId = channelId,
            UserId = tenantBUser.Id,
            Issuer = "iss-other",
            Subject = "other-b",
            IsActive = true
        });

        await db.SaveChangesAsync();

        var originalTenantBPersonId = tenantBUser.PersonId;
        await resolver.LinkUsersAsync(tenantA, sourceA.Id, targetA.Id);

        var tenantBPersonAfter = await resolver.ResolvePersonIdAsync(tenantBUser.Id);
        tenantBPersonAfter.Should().Be(originalTenantBPersonId);
    }

    /// <summary>
    /// Creates an identity resolver backed by an isolated in-memory context.
    /// </summary>
    private static IdentityResolver CreateResolver(out EntityContext db)
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new EntityContext(options);
        return new IdentityResolver(new TestDbContextFactory(options), NullLogger<IdentityResolver>.Instance);
    }

    /// <summary>
    /// Creates a principal with the claims used by these tests.
    /// </summary>
    private static ClaimsPrincipal Principal(string sub, string issuer, string name, string email)
    {
        var claims = new List<Claim>
        {
            new("sub", sub),
            new("iss", issuer),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
}