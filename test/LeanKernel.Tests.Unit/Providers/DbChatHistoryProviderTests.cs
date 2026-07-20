using System.Reflection;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Telemetry;

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
    private static readonly JsonSerializerOptions SJsonOptions = new();

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
        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = string.Empty });
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = "Hi there",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "assistant",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "system",
            Content = "System message",
            Timestamp = DateTimeOffset.UtcNow,
            AuthorName = "system",
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokingCtx = CreateInvokingContext(session: session);

        var result = await provider.InvokeProvideChatHistoryAsync(invokingCtx, CancellationToken.None);

        var messages = result.ToList();
        messages.Should().HaveCount(3);
    }

    /// <summary>
    /// C5: A "tool" turn must rehydrate as <see cref="ChatRole.Tool"/>, not be promoted to User.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ProvideChatHistoryAsync_ToolRole_RehydratesAsChatRoleTool()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        context.Sessions.Add(new SessionEntity
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "tool",
            Content = "Tool output",
            AuthorName = "calculator",
            Timestamp = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var result = (await provider.InvokeProvideChatHistoryAsync(CreateInvokingContext(session: session), CancellationToken.None)).ToList();

        result.Should().ContainSingle();
        result[0].Role.Should().Be(ChatRole.Tool, because: "tool turns must round-trip as ChatRole.Tool, not ChatRole.User");
    }

    /// <summary>
    /// C5: Unknown roles are skipped (not promoted to User) to preserve message provenance.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ProvideChatHistoryAsync_UnknownRole_IsSkippedNotPromotedToUser()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        context.Sessions.Add(new SessionEntity
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        context.Turns.Add(new TurnEntity
        {
            SessionId = sessionId,
            Role = "UNKNOWN_FUTURE_ROLE",
            Content = "some content",
            Timestamp = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var result = await provider.InvokeProvideChatHistoryAsync(CreateInvokingContext(session: session), CancellationToken.None);

        result.Should().BeEmpty(because: "unknown roles must be skipped to avoid corrupting message provenance");
    }

    /// <summary>
    /// M3: A session with more turns than the window limit returns only the most recent turns.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ProvideChatHistoryAsync_ExceedsTurnWindow_ReturnsBoundedCount()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        context.Sessions.Add(new SessionEntity
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        const int turnWindow = 200; // matches DbChatHistoryProvider.RecentTurnWindow
        var overLimit = turnWindow + 20;
        for (var i = 0; i < overLimit; i++)
        {
            context.Turns.Add(new TurnEntity
            {
                SessionId = sessionId,
                Role = "user",
                Content = $"msg {i}",
                Timestamp = DateTimeOffset.UtcNow.AddSeconds(i),
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
            });
        }

        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var result = (await provider.InvokeProvideChatHistoryAsync(CreateInvokingContext(session: session), CancellationToken.None)).ToList();

        result.Count.Should().BeLessThanOrEqualTo(turnWindow,
            because: "unbounded history causes linear prompt-size growth");
    }

    /// <summary>
    /// Integration: user, assistant, and tool turns all round-trip with correct roles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ProvideChatHistoryAsync_ToolCallScenario_AllRolesRoundTrip()
    {
        var (provider, context) = CreateSut();
        var sessionId = Guid.NewGuid();

        var permit = provider.GetPermit();
        context.Sessions.Add(new SessionEntity
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        var ts = DateTimeOffset.UtcNow;
        context.Turns.AddRange(
            new TurnEntity { SessionId = sessionId, Role = "user", Content = "What is 2+2?", Timestamp = ts, CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty } },
            new TurnEntity { SessionId = sessionId, Role = "assistant", Content = "Calculating.", Timestamp = ts.AddSeconds(1), CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty } },
            new TurnEntity { SessionId = sessionId, Role = "tool", Content = "4", AuthorName = "calc", Timestamp = ts.AddSeconds(2), CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty } },
            new TurnEntity { SessionId = sessionId, Role = "assistant", Content = "The answer is 4.", Timestamp = ts.AddSeconds(3), CreatedOn = DateTime.UtcNow, CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty } });
        await context.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var result = (await provider.InvokeProvideChatHistoryAsync(CreateInvokingContext(session: session), CancellationToken.None)).ToList();

        result.Should().HaveCount(4);
        result.Select(m => m.Role.Value).Should().Equal("user", "assistant", "tool", "assistant");
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
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
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
        });
        await dbContext.SaveChangesAsync();

        var session = CreateSession(new Dictionary<string, string?> { ["chatSessionId"] = sessionId.ToString() });
        var invokedCtx = CreateInvokedContext(session: session);

        var act = async () => await provider.InvokeStoreChatHistoryAsync(invokedCtx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StoreChatHistoryAsync_WithAssistantTelemetry_PersistsTurnTelemetry()
    {
        var collector = new TurnTelemetryCollector();
        collector.Capture(new TurnTelemetry
        {
            RequestedModel = "tool",
            ServedModel = "gpt-4o-mini",
            Provider = "openai",
            ModelId = "gpt-4o-mini",
            PromptTokens = 12,
            CompletionTokens = 8,
            TotalTokens = 20,
            ResponseCost = 0.001m,
            Currency = "USD",
            CostIsEstimated = false,
            CapturedAt = DateTimeOffset.UtcNow
        });

        var (provider, dbContext) = CreateSut(collector);
        var session = CreateSession(new Dictionary<string, string?>());
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Telemetry response")
        };

        await provider.InvokeStoreChatHistoryAsync(
            CreateInvokedContext(session: session, responseMessages: responseMessages),
            CancellationToken.None);

        dbContext.TurnTelemetry.Should().ContainSingle();
        var telemetry = dbContext.TurnTelemetry.Single();
        telemetry.ServedModel.Should().Be("gpt-4o-mini");
        telemetry.PromptTokens.Should().Be(12);
        telemetry.TotalTokens.Should().Be(20);
    }

    [Fact]
    public async Task StoreChatHistoryAsync_WithoutCapturedTelemetry_DoesNotPersistTurnTelemetry()
    {
        var collector = new TurnTelemetryCollector();
        var (provider, dbContext) = CreateSut(collector);
        var session = CreateSession(new Dictionary<string, string?>());
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "No telemetry available")
        };

        await provider.InvokeStoreChatHistoryAsync(
            CreateInvokedContext(session: session, responseMessages: responseMessages),
            CancellationToken.None);

        dbContext.Turns.Should().ContainSingle();
        dbContext.TurnTelemetry.Should().BeEmpty();
    }

    private (TestableDbChatHistoryProvider provider, EntityContext context) CreateSut(ITurnTelemetryCollector? collector = null)
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

        return (new TestableDbChatHistoryProvider(factory.Object, permit.Object, collector), context);
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
        {
            bag.SetValue(kvp.Key, kvp.Value, SJsonOptions);
        }

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

    public TestableDbChatHistoryProvider(
        IDbContextFactory<EntityContext> dbContextFactory,
        IPermit permit,
        ITurnTelemetryCollector? telemetryCollector = null)
        : base(dbContextFactory, permit, telemetryCollector)
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