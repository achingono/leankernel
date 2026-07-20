using System.Text.Json;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LeanKernel.Tests.Unit.TestDoubles;

/// <summary>
/// Provides a minimal agent session for unit tests.
/// </summary>
internal sealed class TestAgentSession : AgentSession
{
}

/// <summary>
/// Provides a minimal AI agent implementation for unit tests.
/// </summary>
internal sealed class TestAIAgent : AIAgent
{
    /// <inheritdoc />
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<AgentSession>(new TestAgentSession());
    }

    /// <inheritdoc />
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(JsonDocument.Parse("{}").RootElement);
    }

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<AgentSession>(new TestAgentSession());
    }

    /// <inheritdoc />
    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AgentResponseUpdate(new ChatResponseUpdate { Role = ChatRole.Assistant });
        await Task.CompletedTask;
    }
}