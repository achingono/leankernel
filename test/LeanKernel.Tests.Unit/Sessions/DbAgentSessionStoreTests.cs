using FluentAssertions;
using LeanKernel.Data;
using LeanKernel.Gateway.Sessions;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Sessions;

public class DbAgentSessionStoreTests
{
    private static (DbAgentSessionStore store, EntityContext context) CreateSut()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var entityContext = new EntityContext(options);
        return (new DbAgentSessionStore(entityContext), entityContext);
    }

    private static ChatClientAgent CreateStubAgent()
    {
        return new ChatClientAgent(
            new Mock<IChatClient>().Object,
            new ChatClientAgentOptions(),
            null,
            null);
    }

    [Fact]
    public async Task GetSessionAsync_WhenNoEntity_ReturnsNewSession()
    {
        var (store, _) = CreateSut();
        var agent = CreateStubAgent();

        var session = await store.GetSessionAsync(agent, "nonexistent-conversation-id");

        session.Should().NotBeNull();
        session.Should().BeOfType<ChatClientAgentSession>();
    }

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

    [Fact]
    public async Task SaveSessionAsync_CreatesEntityInDatabase()
    {
        var (store, context) = CreateSut();
        var agent = CreateStubAgent();
        var conversationId = $"test-conv-{Guid.NewGuid():N}";
        var session = await agent.CreateSessionAsync();

        await store.SaveSessionAsync(agent, conversationId, session);

        var entity = await context.AgentSessions.FindAsync(conversationId);
        entity.Should().NotBeNull();
        entity!.StateJson.Should().NotBeNullOrEmpty();
    }

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
}
