using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Tests covering DbChatHistoryProvider for new code coverage.
/// </summary>
public class DbChatHistoryProviderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private static readonly JsonSerializerOptions s_jsonOptions = new();

    public DbChatHistoryProviderTests() => _connection.Open();
    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ProvideChatHistoryAsync_NullSession_Throws()
    {
        var (provider, _) = CreateSut();
        var context = CreateInvokingContext(session: null);

        var act = async () => await provider.InvokeProvideChatHistoryAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_NoChatSessionId_ReturnsEmpty()
    {
        var (provider, _) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?>());
        var context = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(context, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_EmptyChatSessionId_ReturnsEmpty()
    {
        var (provider, _) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = "" });
        var context = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(context, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_InvalidGuidSessionId_ReturnsEmpty()
    {
        var (provider, _) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = "not-a-guid" });
        var context = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(context, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_OwnedSession_ReturnsTurns()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        var sessionEntity = new SessionEntity
        {
            Id = sessionId,
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        };
        context.Sessions.Add(sessionEntity);
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "user",
            Content = "Hello",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "test-user",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = "Hi there",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "assistant",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "system",
            Content = "System message",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "system",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokingCtx = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(invokingCtx, CancellationToken.None);

        var messages = result.ToList();
        messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_OtherRoleDefaultsToUser()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        var sessionEntity = new SessionEntity
        {
            Id = sessionId,
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        };
        context.Sessions.Add(sessionEntity);
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "tool",
            Content = "Tool output",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "tool",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokingCtx = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(invokingCtx, CancellationToken.None);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_UnownedSession_ReturnsEmpty()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();
        var unownedTenantId = Guid.NewGuid();
        var unownedUserId = Guid.NewGuid();
        var unownedChannelId = Guid.NewGuid();

        SeedIdentityGraph(context, unownedTenantId, unownedUserId, unownedChannelId);

        context.Sessions.Add(new SessionEntity
        {
            Id = sessionId,
            TenantId = unownedTenantId,
            UserId = unownedUserId,
            ChannelId = unownedChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokingCtx = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(invokingCtx, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_NullSession_Throws()
    {
        var (provider, _) = CreateSut();
        var context = CreateInvokedContext(session: null);

        var act = async () => await provider.InvokeStoreChatHistoryAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_NoSessionId_CreatesNewSession()
    {
        var (provider, dbContext) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?>());
        var invokedCtx = CreateInvokedContext(session: session);

        await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        dbContext.Sessions.Should().ContainSingle();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_InvalidSessionId_CreatesNewSession()
    {
        var (provider, dbContext) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = "invalid" });
        var invokedCtx = CreateInvokedContext(session: session);

        await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        dbContext.Sessions.Should().ContainSingle();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_ValidSessionId_UpdatesExisting()
    {
        var (provider, dbContext) = CreateSut();
        var sessionId = Guid.NewGuid();
        var permit = provider.GetPermit();
        dbContext.Sessions.Add(new SessionEntity
        {
            Id = sessionId,
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Test message")
        };
        var invokedCtx = CreateInvokedContext(session: session, requestMessages: requestMessages);

        await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        dbContext.Turns.Should().ContainSingle();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_WithResponseMessages_PersistsAssistantTurns()
    {
        var (provider, dbContext) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = "new" });
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Response")
        };
        var invokedCtx = CreateInvokedContext(session: session, responseMessages: responseMessages);

        await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        dbContext.Turns.Should().ContainSingle();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_NullRequestMessages_DoesNotThrow()
    {
        var (provider, _) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?>());
        var invokedCtx = CreateInvokedContext(session: session, requestMessages: null);

        var act = async () => await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_NullResponseMessages_DoesNotThrow()
    {
        var (provider, _) = CreateSut();
        var session = CreateSession(new Dictionary<string, string?>());
        var invokedCtx = CreateInvokedContext(session: session, responseMessages: null);

        var act = async () => await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_UnownedSession_Throws()
    {
        var (provider, dbContext) = CreateSut();
        var sessionId = Guid.NewGuid();
        var unownedTenantId = Guid.NewGuid();
        var unownedUserId = Guid.NewGuid();
        var unownedChannelId = Guid.NewGuid();

        SeedIdentityGraph(dbContext, unownedTenantId, unownedUserId, unownedChannelId);

        dbContext.Sessions.Add(new SessionEntity
        {
            Id = sessionId,
            TenantId = unownedTenantId,
            UserId = unownedUserId,
            ChannelId = unownedChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "" }
        });
        await dbContext.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokedCtx = CreateInvokedContext(session: session);

        var act = async () => await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private (TestableDbChatHistoryProvider provider, EntityContext context) CreateSut()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new EntityContext(options);
        context.Database.EnsureCreated();

        var factory = new Mock<IDbContextFactory<EntityContext>>();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.UserId).Returns(userId);
        permit.Setup(p => p.ChannelId).Returns(channelId);

        SeedIdentityGraph(context, tenantId, userId, channelId);

        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EntityContext(options));

        return (new TestableDbChatHistoryProvider(factory.Object, permit.Object), context);
    }

    private static void SeedIdentityGraph(EntityContext context, Guid tenantId, Guid userId, Guid channelId)
    {
        context.Tenants.Add(new TenantEntity
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Description = "test tenant",
            HostName = $"{tenantId:N}.local",
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        });

        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = $"{userId:N}@example.test",
            UserName = $"user-{userId:N}",
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            IsActive = true,
            IsLockedOut = false,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" },
            Issuer = "tests",
            Subject = userId.ToString("N"),
            IsGuest = false
        });

        context.Channels.Add(new ChannelEntity
        {
            Id = channelId,
            Name = $"channel-{channelId:N}"
        });

        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private static ChatClientAgentSession CreateSession(Dictionary<string, string?> stateBag)
    {
        var bag = new AgentSessionStateBag();
        foreach (var kvp in stateBag)
            bag.SetValue(kvp.Key, kvp.Value, s_jsonOptions);
        return (ChatClientAgentSession)Activator.CreateInstance(
            typeof(ChatClientAgentSession),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, [null, bag], null)!;
    }

    private static AgentSession CreateDefaultSession()
    {
        return (AgentSession)Activator.CreateInstance(
            typeof(ChatClientAgentSession),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, Array.Empty<object?>(), null)!;
    }

    private static ChatHistoryProvider.InvokingContext CreateInvokingContext(
        AgentSession? session = null)
    {
        var agent = CreateStubAgent();

        var ctor = typeof(ChatHistoryProvider).GetNestedType("InvokingContext", BindingFlags.Public)!
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
        return (ChatHistoryProvider.InvokingContext)ctor.Invoke(
            [agent, session, Array.Empty<ChatMessage>()]);
    }

    private static ChatHistoryProvider.InvokedContext CreateInvokedContext(
        AgentSession? session = null,
        IEnumerable<ChatMessage>? requestMessages = null,
        IEnumerable<ChatMessage>? responseMessages = null)
    {
        var agent = CreateStubAgent();

        var ctor = typeof(ChatHistoryProvider).GetNestedType("InvokedContext", BindingFlags.Public)!
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 4);
        return (ChatHistoryProvider.InvokedContext)ctor.Invoke(
            [
                agent,
                session,
                requestMessages ?? Array.Empty<ChatMessage>(),
                responseMessages ?? Array.Empty<ChatMessage>()
            ]);
    }

    private static ChatClientAgent CreateStubAgent()
    {
        return new ChatClientAgent(
            new Mock<IChatClient>().Object,
            new ChatClientAgentOptions(),
            null,
            null);
    }
}

/// <summary>
/// Test subclass that exposes protected methods.
/// </summary>
internal sealed class TestableDbChatHistoryProvider : DbChatHistoryProvider
{
    private readonly IPermit _permit;

    public TestableDbChatHistoryProvider(IDbContextFactory<EntityContext> dbContextFactory, IPermit permit)
        : base(dbContextFactory, permit)
    {
        _permit = permit;
    }

    public IPermit GetPermit() => _permit;

    public async ValueTask<IEnumerable<ChatMessage>> InvokeProvideChatHistoryAsync(
        ChatHistoryProvider.InvokingContext context,
        CancellationToken cancellationToken)
    {
        return await ProvideChatHistoryAsync(context, cancellationToken);
    }

    public async ValueTask InvokeStoreChatHistoryAsync(
        ChatHistoryProvider.InvokedContext context,
        CancellationToken cancellationToken)
    {
        await StoreChatHistoryAsync(context, cancellationToken);
    }
}
