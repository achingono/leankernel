using FluentAssertions;
using LeanKernel;
using LeanKernel.Data;
using LeanKernel.Gateway.Sessions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Sessions;

/// <summary>
/// Covers database-backed agent session state storage.
/// </summary>
public class DbAgentStateStoreTests
{
    /// <summary>
    /// Creates a store and backing context with generated ownership identifiers.
    /// </summary>
    private static (DbAgentStateStore store, EntityContext context) CreateSut(
        Guid? tenantId = null,
        Guid? userId = null,
        Guid? channelId = null)
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var entityContext = new EntityContext(options);

        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId ?? Guid.NewGuid());
        permit.Setup(p => p.UserId).Returns(userId ?? Guid.NewGuid());
        permit.Setup(p => p.ChannelId).Returns(channelId ?? Guid.NewGuid());

        return (new DbAgentStateStore(entityContext, permit.Object), entityContext);
    }

    /// <summary>
    /// Creates a minimal chat client agent for session tests.
    /// </summary>
    private static ChatClientAgent CreateStubAgent()
    {
        return new ChatClientAgent(
            new Mock<IChatClient>().Object,
            new ChatClientAgentOptions(),
            null,
            null);
    }

    /// <summary>
    /// Verifies missing persisted state returns a new agent session.
    /// </summary>
    [Fact]
    public async Task GetSessionAsync_WhenNoEntity_ReturnsNewSession()
    {
        var (store, _) = CreateSut();
        var agent = CreateStubAgent();

        var session = await store.GetSessionAsync(agent, "nonexistent-conversation-id");

        session.Should().NotBeNull();
        session.Should().BeOfType<ChatClientAgentSession>();
    }

    /// <summary>
    /// Verifies saved sessions can be loaded again.
    /// </summary>
    [Fact]
    public async Task SaveSessionAsync_ThenGetSessionAsync_RoundTrips()
    {
        var (store, _) = CreateSut();
        var agent = CreateStubAgent();
        var conversationId = $"test-conv-{Guid.NewGuid():N}";
        var session = await agent.CreateSessionAsync();

        await store.SaveSessionAsync(agent, conversationId, session);

        var restored = await store.GetSessionAsync(agent, conversationId);

        restored.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies saving a session creates a backing database entity.
    /// </summary>
    [Fact]
    public async Task SaveSessionAsync_CreatesEntityInDatabase()
    {
        var (store, context) = CreateSut();
        var agent = CreateStubAgent();
        var conversationId = $"test-conv-{Guid.NewGuid():N}";
        var session = await agent.CreateSessionAsync();

        await store.SaveSessionAsync(agent, conversationId, session);

        var entity = await context.AgentStates.FindAsync(conversationId);
        entity.Should().NotBeNull();
        entity!.StateJson.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies different conversations persist isolated state.
    /// </summary>
    [Fact]
    public async Task SaveSessionAsync_DifferentConversations_AreIsolated()
    {
        var (store, _) = CreateSut();
        var agent = CreateStubAgent();
        var convId1 = $"test-conv-{Guid.NewGuid():N}";
        var convId2 = $"test-conv-{Guid.NewGuid():N}";
        var session1 = await agent.CreateSessionAsync();
        var session2 = await agent.CreateSessionAsync();

        await store.SaveSessionAsync(agent, convId1, session1);
        await store.SaveSessionAsync(agent, convId2, session2);

        var restored1 = await store.GetSessionAsync(agent, convId1);
        var restored2 = await store.GetSessionAsync(agent, convId2);

        restored1.Should().NotBeNull();
        restored2.Should().NotBeNull();
        restored1.Should().NotBeSameAs(restored2);
    }

    /// <summary>
    /// Verifies ownership metadata is persisted with the saved session.
    /// </summary>
    [Fact]
    public async Task SaveSessionAsync_PopulatesOwnershipMetadata()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var (store, context) = CreateSut(tenantId, userId, channelId);
        var agent = CreateStubAgent();
        var conversationId = $"test-conv-{Guid.NewGuid():N}";
        var session = await agent.CreateSessionAsync();

        await store.SaveSessionAsync(agent, conversationId, session);

        var entity = await context.AgentStates.FindAsync(conversationId);
        entity.Should().NotBeNull();
        entity!.TenantId.Should().Be(tenantId);
        entity.UserId.Should().Be(userId);
        entity.ChannelId.Should().Be(channelId);
        entity.CreatedOn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
