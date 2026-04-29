using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Main reasoning loop. Takes a message, obtains gated context
/// from the Archivist, sends to LLM via MAF ChatClientAgent, and
/// persists learned facts back to the wiki.
/// </summary>
public sealed class ThinkerService : IThinkerService
{
    private readonly IContextGatekeeper _gatekeeper;
    private readonly ISessionStore _sessions;
    private readonly IWikiStore _wiki;
    private readonly AgentFactory _agentFactory;
    private readonly ToolFunctionAdapter _toolAdapter;
    private readonly PromptAssembler _promptAssembler;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    public ThinkerService(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IWikiStore wiki,
        AgentFactory agentFactory,
        ToolFunctionAdapter toolAdapter,
        PromptAssembler promptAssembler,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger)
    {
        _gatekeeper = gatekeeper;
        _sessions = sessions;
        _wiki = wiki;
        _agentFactory = agentFactory;
        _toolAdapter = toolAdapter;
        _promptAssembler = promptAssembler;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct)
    {
        // 1. Resolve session
        var sessionId = await _sessions.GetOrCreateSessionIdAsync(
            message.ChannelId, message.SenderId, ct);

        // 2. Record inbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "user",
            Content = message.Content,
            Timestamp = message.Timestamp
        }, ct);

        // 3. Gate context (deny-by-default)
        var budget = ContextBudget.FromModelWindow(_config.LiteLlm.ContextWindowTokens);
        var context = await _gatekeeper.GateContextAsync(message, budget, sessionId, ct);

        _logger.LogInformation(
            "Context assembled: ~{Tokens} tokens, {Wiki} wiki, {Turns} turns, {Excluded} exclusions",
            context.EstimatedTotalTokens, context.WikiLeanKernels.Count,
            context.History.Count, context.ExclusionLog.Count);

        // 4. Call LLM via MAF ChatClientAgent
        string response;
        try
        {
            var instructions = _promptAssembler.AssembleSystemMessage(context);
            var tools = _toolAdapter.BuildTools();
            var agent = _agentFactory.CreateAgent(instructions, tools);

            // Build history messages from gated context
            var messages = BuildMessages(context.History, message.Content);
            var agentResponse = await agent.RunAsync(messages, cancellationToken: ct);

            response = agentResponse.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM invocation failed — returning error message");
            response = "I'm sorry, I encountered an error processing your request. Please try again.";
        }

        // 5. Record outbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // 6. Extract and persist 5W1H facts from the exchange
        try
        {
            var sourceId = $"conversation:{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss}";
            var facts = WikiExtractor.ExtractFacts(message.Content, response, sourceId);
            if (facts.Count > 0)
            {
                await _wiki.IngestFactsAsync(facts, ct);
                _logger.LogDebug("Extracted {Count} wiki entries from conversation", facts.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction failed — continuing without persistence");
        }

        return response;
    }

    /// <summary>
    /// Convert gated conversation history + current query into ChatMessage list.
    /// The system message is handled by the agent's <c>instructions</c> parameter.
    /// </summary>
    internal static IEnumerable<ChatMessage> BuildMessages(
        IReadOnlyList<ConversationTurn> history,
        string currentQuery)
    {
        foreach (var turn in history)
        {
            var role = turn.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            yield return new ChatMessage(role, turn.Content);
        }

        yield return new ChatMessage(ChatRole.User, currentQuery);
    }
}
