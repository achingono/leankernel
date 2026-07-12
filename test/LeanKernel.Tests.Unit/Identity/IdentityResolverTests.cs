using System.Security.Claims;
using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Gateway.Identity;
using LeanKernel.Tests.Unit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

public class IdentityResolverTests
{
    [Fact]
    public async Task ResolveTenantAsync_BlankHost_ReturnsNull()
    {
        var resolver = CreateResolver(out _);

        var tenant = await resolver.ResolveTenantAsync(" ");

        tenant.Should().BeNull();
    }

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

    [Fact]
    public async Task ResolveOrCreateUserAsync_WithoutSubject_Throws()
    {
        var resolver = CreateResolver(out _);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));

        var act = () => resolver.ResolveOrCreateUserAsync(principal);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveGuestAndChannel_CreateThenReuse()
    {
        var resolver = CreateResolver(out var db);

        var guest1 = await resolver.ResolveGuestUserAsync(Guid.NewGuid(), "anonymous");
        var guest2 = await resolver.ResolveGuestUserAsync(Guid.NewGuid(), "anonymous");
        guest2.Id.Should().Be(guest1.Id);

        var channel1 = await resolver.ResolveOrCreateChannelAsync("openai-http");
        var channel2 = await resolver.ResolveOrCreateChannelAsync("openai-http");
        channel2.Id.Should().Be(channel1.Id);

        db.Users.Count().Should().Be(1);
        db.Channels.Count().Should().Be(1);
    }

    private static IdentityResolver CreateResolver(out EntityContext db)
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new EntityContext(options);
        return new IdentityResolver(new TestDbContextFactory(options), NullLogger<IdentityResolver>.Instance);
    }

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
