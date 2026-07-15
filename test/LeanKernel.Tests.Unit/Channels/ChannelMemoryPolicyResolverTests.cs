using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Tests.Unit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Channels;

/// <summary>
/// Tests for <see cref="ChannelMemoryPolicyResolver"/> — directional AND memory sharing policy resolution.
/// </summary>
public sealed class ChannelMemoryPolicyResolverTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ChannelMemoryPolicyResolverTests() => _connection.Open();

    private (ChannelMemoryPolicyResolver resolver, EntityContext db) CreateResolver()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        context.Database.EnsureCreated();

        var factory = new TestDbContextFactory(options);
        var resolver = new ChannelMemoryPolicyResolver(factory, Mock.Of<ILogger<ChannelMemoryPolicyResolver>>());
        return (resolver, context);
    }

    private static ChannelEntity AddChannel(EntityContext db, string name)
    {
        var channel = new ChannelEntity { Id = Guid.NewGuid(), Name = name };
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private static void AddPolicy(EntityContext db, Guid tenantId, ChannelEntity channel, string share = "*", string access = "*")
    {
        EnsureTenant(db, tenantId);

        db.ChannelMemoryPolicies.Add(new ChannelMemoryPolicyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelId = channel.Id,
            Share = share,
            Access = access
        });
        db.SaveChanges();
    }

    private static void EnsureTenant(EntityContext db, Guid tenantId)
    {
        if (db.Tenants.Any(t => t.Id == tenantId))
        {
            return;
        }

        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Description = "test tenant",
            HostName = $"{tenantId:N}.example.test",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge
            {
                Id = Guid.Empty,
                FullName = "Test",
                Email = "test@example.com"
            }
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetReadableChannelsAsync_WildcardDefault_ReturnsAllChannels()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");
        var chC = AddChannel(db, "channel-c");
        AddPolicy(db, tenantId, chA);
        AddPolicy(db, tenantId, chB);
        AddPolicy(db, tenantId, chC);

        var readable = await resolver.GetReadableChannelsAsync(tenantId, "channel-a");

        readable.Should().HaveCount(3, "because wildcard default means all channels are readable");
        readable.Should().Contain("channel-a");
        readable.Should().Contain("channel-b");
        readable.Should().Contain("channel-c");
    }

    [Fact]
    public async Task GetReadableChannelsAsync_NoPolicy_UsesWildcardDefaults()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        AddChannel(db, "channel-a");
        AddChannel(db, "channel-b");

        var readable = await resolver.GetReadableChannelsAsync(tenantId, "channel-a");

        readable.Should().HaveCount(2, "because missing policy rows default to wildcard sharing/access");
        readable.Should().Contain("channel-a");
        readable.Should().Contain("channel-b");
    }

    [Fact]
    public async Task GetReadableChannelsAsync_DirectionalAnd_BothSidesMustOptIn()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");
        // A shares to B, but B does not access A
        AddPolicy(db, tenantId, chA, share: "channel-b", access: "*");
        AddPolicy(db, tenantId, chB, share: "*", access: "");

        var readableFromA = await resolver.GetReadableChannelsAsync(tenantId, "channel-a");
        var readableFromB = await resolver.GetReadableChannelsAsync(tenantId, "channel-b");

        // A can read B (A accesses B via wildcard, B shares to everyone via wildcard)
        readableFromA.Should().Contain("channel-b");
        // B cannot read A (B has empty access list)
        readableFromB.Should().NotContain("channel-a");
    }

    [Fact]
    public async Task GetReadableChannelsAsync_SelfAlwaysReadable()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        AddChannel(db, "channel-a");

        var readable = await resolver.GetReadableChannelsAsync(tenantId, "channel-a");

        readable.Should().Contain("channel-a", "because a channel always reads itself");
    }

    [Fact]
    public async Task GetMutuallyVisibleChannelsAsync_WildcardDefault_ReturnsAllChannels()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");
        AddPolicy(db, tenantId, chA);
        AddPolicy(db, tenantId, chB);

        var visible = await resolver.GetMutuallyVisibleChannelsAsync(tenantId, "channel-a");

        visible.Should().HaveCount(2);
        visible.Should().Contain("channel-a");
        visible.Should().Contain("channel-b");
    }

    [Fact]
    public async Task GetMutuallyVisibleChannelsAsync_OneWaySharing_NotMutuallyVisible()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");
        // A shares to B, but B does not share to A
        AddPolicy(db, tenantId, chA, share: "channel-b", access: "*");
        AddPolicy(db, tenantId, chB, share: "*", access: "");

        var visible = await resolver.GetMutuallyVisibleChannelsAsync(tenantId, "channel-a");

        visible.Should().HaveCount(1, "because mutual visibility requires both directions");
        visible.Should().Contain("channel-a");
    }

    [Fact]
    public async Task GetMutuallyVisibleChannelsAsync_EmptyLists_Isolated()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");
        AddPolicy(db, tenantId, chA, share: "", access: "");
        AddPolicy(db, tenantId, chB, share: "", access: "");

        var visible = await resolver.GetMutuallyVisibleChannelsAsync(tenantId, "channel-a");

        visible.Should().HaveCount(1, "because empty lists mean full isolation");
        visible.Should().Contain("channel-a");
    }

    [Fact]
    public async Task SignalPolicyChangedAsync_CompletesWithoutError()
    {
        var (resolver, _) = CreateResolver();

        var act = async () => await resolver.SignalPolicyChangedAsync(Guid.NewGuid(), "channel-a");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetReadableChannelsAsync_CaseInsensitiveChannelNames()
    {
        var (resolver, db) = CreateResolver();
        var tenantId = Guid.NewGuid();
        var chA = AddChannel(db, "Channel-A");
        var chB = AddChannel(db, "Channel-B");
        AddPolicy(db, tenantId, chA, share: "Channel-B", access: "Channel-B");
        AddPolicy(db, tenantId, chB, share: "Channel-A", access: "Channel-A");

        var readable = await resolver.GetReadableChannelsAsync(tenantId, "channel-a");

        readable.Should().Contain("channel-b", "because channel name matching should be case-insensitive");
    }

    [Fact]
    public async Task GetReadableChannelsAsync_UsesTenantScopedPolicies()
    {
        var (resolver, db) = CreateResolver();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var chA = AddChannel(db, "channel-a");
        var chB = AddChannel(db, "channel-b");

        AddPolicy(db, tenantA, chA, share: "", access: "");
        AddPolicy(db, tenantA, chB, share: "", access: "");
        AddPolicy(db, tenantB, chA, share: "*", access: "*");
        AddPolicy(db, tenantB, chB, share: "*", access: "*");

        var readableForTenantA = await resolver.GetReadableChannelsAsync(tenantA, "channel-a");
        var readableForTenantB = await resolver.GetReadableChannelsAsync(tenantB, "channel-a");

        readableForTenantA.Should().ContainSingle().Which.Should().Be("channel-a");
        readableForTenantB.Should().Contain("channel-b");
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
