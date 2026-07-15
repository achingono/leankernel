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
    [Fact]
    public async Task ResolveTenantAsync_ReturnsOnlyActiveTenant()
    {
        var resolver = CreateResolver(out var db);
        db.Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(), Name = "A", HostName = "a.test", IsActive = false,
            CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = "" }
        });
        db.Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(), Name = "B", HostName = "b.test", IsActive = true,
            CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = "" }
        });
        await db.SaveChangesAsync();

        (await resolver.ResolveTenantAsync("a.test")).Should().BeNull();
        (await resolver.ResolveTenantAsync("b.test"))!.HostName.Should().Be("b.test");
    }

    /// <summary>
    /// Verifies users are created once and then reused.
    /// </summary>
    [Fact]
    public async Task ResolveOrCreateUserAsync_CreatesAndFindsUser()
    {
        var resolver = CreateResolver(out var db);
        var principal = Principal("sub-1", "issuer-1", "Jane", "jane@test");

        var created = await resolver.ResolveOrCreateUserAsync(principal);
        created.Subject.Should().Be("sub-1");

        var fetched = await resolver.ResolveOrCreateUserAsync(principal);
        fetched.Id.Should().Be(created.Id);
        fetched.LastActivity.Should().NotBeNull();
        db.Users.Count().Should().Be(1);
    }

    /// <summary>
    /// Verifies missing subject claims are rejected.
    /// </summary>
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
    [Fact]
    public async Task ResolveGuestAndChannel_CreateThenReuse()
    {
        var resolver = CreateResolver(out var db);

        var tenantId = Guid.NewGuid();
        var guest1 = await resolver.ResolveGuestUserAsync(tenantId, "anonymous", "session-1");
        var guest2 = await resolver.ResolveGuestUserAsync(tenantId, "anonymous", "session-1");
        guest2.Id.Should().Be(guest1.Id);

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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = "" }
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
