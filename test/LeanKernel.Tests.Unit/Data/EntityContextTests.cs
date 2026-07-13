using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeanKernel.Tests.Unit.Data;

/// <summary>
/// Covers basic entity persistence behavior for <see cref="EntityContext"/>.
/// </summary>
public class EntityContextTests
{
    /// <summary>
    /// Creates an isolated in-memory entity context.
    /// </summary>
    private static EntityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new EntityContext(options);
    }

    /// <summary>
    /// Verifies agent state entities can be saved and queried.
    /// </summary>
    [Fact]
    public async Task AgentStates_CanAddAndQuery()
    {
        using var ctx = CreateContext();
        var entity = new AgentStateEntity
        {
            ScopedConversationId = "test-scoped-id-1",
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            StateJson = "{}",
            CreatedOn = DateTimeOffset.UtcNow,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        ctx.AgentStates.Add(entity);
        await ctx.SaveChangesAsync();

        var result = await ctx.AgentStates
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ScopedConversationId == "test-scoped-id-1");

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(entity.TenantId);
        result.UserId.Should().Be(entity.UserId);
        result.ChannelId.Should().Be(entity.ChannelId);
    }

    /// <summary>
    /// Verifies duplicate tracked agent state keys are rejected.
    /// </summary>
    [Fact]
    public void AgentStates_DuplicateKey_Throws()
    {
        using var ctx = CreateContext();
        var id = $"dup-test-{Guid.NewGuid():N}";

        ctx.AgentStates.Add(new AgentStateEntity
        {
            ScopedConversationId = id,
            StateJson = "{}"
        });

        var act = () => ctx.AgentStates.Add(new AgentStateEntity
        {
            ScopedConversationId = id,
            StateJson = "{}"
        });

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Verifies session entities can be saved with related navigation data.
    /// </summary>
    [Fact]
    public async Task Sessions_CanAddWithNavigationProps()
    {
        using var ctx = CreateContext();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var user = new UserEntity
        {
            Id = userId,
            Email = "a@b.com",
            UserName = "a",
            FullName = "A",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge()
        };
        var channel = new ChannelEntity { Id = channelId, Name = "test-channel" };
        var tenant = new TenantEntity
        {
            Id = tenantId,
            Name = "Test",
            HostName = "test.local",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge()
        };

        ctx.Users.Add(user);
        ctx.Channels.Add(channel);
        ctx.Tenants.Add(tenant);

        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            User = user,
            Channel = channel,
            Tenant = tenant
        };
        ctx.Sessions.Add(session);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Sessions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Channel)
            .FirstOrDefaultAsync(s => s.Id == session.Id);

        loaded.Should().NotBeNull();
        loaded!.User.Email.Should().Be("a@b.com");
        loaded.Channel.Name.Should().Be("test-channel");
    }
}
