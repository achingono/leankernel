using System.Collections.Concurrent;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Gateway.Requests;

using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

/// <summary>
/// Unit tests for <see cref="ScopedKeyedAIAgentProxy"/>.
/// </summary>
public class ScopedKeyedAIAgentProxyTests
{
    [Fact]
    public async Task RunAsync_WithoutActiveHttpContext_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor();
        var proxy = new ScopedKeyedAIAgentProxy("leankernel", accessor);

        var act = async () => await proxy.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_WithDifferentRequestScopes_ResolvesDifferentAgentInstances()
    {
        var recorder = new ConcurrentBag<Guid>();
        var services = new ServiceCollection();
        services.AddKeyedScoped<AIAgent>("leankernel", (_, _) => new RecordingAgent(recorder));

        using var provider = services.BuildServiceProvider();
        var accessor = new HttpContextAccessor();
        var proxy = new ScopedKeyedAIAgentProxy("leankernel", accessor);

        using (var scope = provider.CreateScope())
        {
            accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            await proxy.RunAsync([new ChatMessage(ChatRole.User, "first")]);
        }

        using (var scope = provider.CreateScope())
        {
            accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            await proxy.RunAsync([new ChatMessage(ChatRole.User, "second")]);
        }

        recorder.Should().HaveCount(2);
        recorder.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task SessionOperations_DelegateToScopedInnerAgent()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<AIAgent>("leankernel", (_, _) => new SessionAgent());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        };
        var proxy = new ScopedKeyedAIAgentProxy("leankernel", accessor);

        var session = await proxy.CreateSessionAsync();
        var serialized = await proxy.SerializeSessionAsync(session);
        var restored = await proxy.DeserializeSessionAsync(serialized);

        session.Should().NotBeNull();
        restored.Should().NotBeNull();
    }

    private sealed class RecordingAgent(ConcurrentBag<Guid> recorder) : AIAgent
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new RecordingSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new RecordingSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            recorder.Add(_instanceId);
            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            recorder.Add(_instanceId);
            yield return new AgentResponseUpdate(new ChatResponseUpdate { Role = ChatRole.Assistant });
            await Task.CompletedTask;
        }
    }

    private sealed class SessionAgent : AIAgent
    {
        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new RecordingSession());

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AgentSession>(new RecordingSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AgentResponseUpdate(new ChatResponseUpdate { Role = ChatRole.Assistant });
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingSession : AgentSession
    {
    }
}