using System.Text.Json;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Resolves a keyed <see cref="AIAgent"/> from the current request scope for each invocation.
/// </summary>
public sealed class ScopedKeyedAIAgentProxy(string agentName, IHttpContextAccessor httpContextAccessor) : AIAgent
{
    /// <summary>
    /// Gets the keyed agent name used for request-scope resolution.
    /// </summary>
    public override string? Name => agentName;

    /// <inheritdoc />
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => ResolveInnerAgent().CreateSessionAsync(cancellationToken);

    /// <inheritdoc />
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => ResolveInnerAgent().SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);

    /// <inheritdoc />
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => ResolveInnerAgent().DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);

    /// <inheritdoc />
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => ResolveInnerAgent().RunAsync(messages, session, options, cancellationToken);

    /// <inheritdoc />
    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => ResolveInnerAgent().RunStreamingAsync(messages, session, options, cancellationToken);

    private AIAgent ResolveInnerAgent()
    {
        var requestServices = httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("Chat completions agent resolution requires an active HTTP request scope.");

        return requestServices.GetRequiredKeyedService<AIAgent>(agentName);
    }
}