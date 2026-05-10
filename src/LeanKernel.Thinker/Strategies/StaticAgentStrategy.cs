using Microsoft.Extensions.AI;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Invokes the default configured model without dynamic routing.
/// </summary>
public sealed class StaticAgentStrategy : IAgentStrategy
{
    private readonly AgentFactory _agentFactory;
    private readonly ISessionStore _sessions;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticAgentStrategy" /> class.
    /// </summary>
    /// <param name="agentFactory">The factory used to create chat agents.</param>
    /// <param name="sessions">The session store used to persist diagnostics metadata.</param>
    public StaticAgentStrategy(AgentFactory agentFactory, ISessionStore sessions)
    {
        _agentFactory = agentFactory;
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string Name => "static";

    /// <inheritdoc />
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct)
    {
        var agent = _agentFactory.CreateAgent(context.Instructions, context.Tools);
        var messages = BuildMessages(context.Context.History, context.Message.Content);
        var agentSession = await agent.CreateSessionAsync(ct);
        var agentResponse = await agent.RunAsync(messages, agentSession, cancellationToken: ct);

        var response = agentResponse.Text ?? string.Empty;

        if (agentSession?.StateBag is { } bag)
        {
            string[] diagKeys = ["last_duration_ms", "last_message_count", "last_tool_calls"];
            foreach (var key in diagKeys)
            {
                var val = bag.GetValue<string>(key);
                if (val is not null)
                {
                    await _sessions.SetMetadataAsync(context.SessionId, key, val, ct);
                }
            }
        }

        return response;
    }

    /// <summary>
    /// Converts gated conversation history and the current query into chat messages.
    /// </summary>
    /// <param name="history">The conversation history selected for the turn.</param>
    /// <param name="currentQuery">The current user query.</param>
    /// <returns>The chat messages to send to the model.</returns>
    internal static IEnumerable<ChatMessage> BuildMessages(
        IReadOnlyList<ConversationTurn> history,
        string currentQuery)
    {
        foreach (var msg in history.ToChatMessages())
        {
            yield return msg;
        }

        yield return new ChatMessage(ChatRole.User, currentQuery);
    }
}
