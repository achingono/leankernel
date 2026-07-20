using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Data;

/// <summary>
/// Covers basic entity persistence behavior for <see cref="EntityContext"/>.
/// </summary>
public class EntityContextTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public EntityContextTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private EntityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var ctx = new TestEntityContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    /// <summary>
    /// Verifies agent state entities can be saved and queried.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
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
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        };
        var channel = new ChannelEntity { Id = channelId, Name = "test-channel" };
        var tenant = new TenantEntity
        {
            Id = tenantId,
            Name = "Test",
            HostName = "test.local",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
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
            Tenant = tenant,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
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

    /// <summary>
    /// M2: The EF model must not generate a TenantEntityId shadow FK on SessionEntity.
    /// </summary>
    [Fact]
    public void SessionEntity_HasNoShadowTenantEntityIdProperty()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new EntityContext(options);

        var sessionEntityType = ctx.Model.FindEntityType(typeof(SessionEntity))!;
        var shadowProps = sessionEntityType.GetProperties()
            .Where(p => p.IsShadowProperty())
            .Select(p => p.Name)
            .ToList();

        shadowProps.Should().NotContain("TenantEntityId",
            because: "the duplicate shadow FK must have been removed to eliminate the redundant TenantEntityId column");
    }

    /// <summary>
    /// Test-specific EntityContext that overrides RowVersion to ValueGeneratedNever
    /// so SQLite (which lacks native rowversion support) can accept explicit values.
    /// </summary>
    private sealed class TestEntityContext(DbContextOptions<EntityContext> options) : EntityContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AgentStateEntity>().Property(e => e.RowVersion).ValueGeneratedNever();
        }
    }
}