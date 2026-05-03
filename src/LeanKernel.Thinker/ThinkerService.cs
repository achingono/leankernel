using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Routing;

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
    private readonly ModelRoutingService? _routing;
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
        ILogger<ThinkerService> logger,
        ModelRoutingService? routing = null)
    {
        _gatekeeper = gatekeeper;
        _sessions = sessions;
        _wiki = wiki;
        _agentFactory = agentFactory;
        _toolAdapter = toolAdapter;
        _promptAssembler = promptAssembler;
        _routing = routing;
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

        // 4. Call LLM — via intelligent routing when enabled, otherwise static default.
        string response;
        try
        {
            var instructions = _promptAssembler.AssembleSystemMessage(context);
            var tools = _toolAdapter.BuildTools();

            if (_config.Routing.Enabled && _routing is not null)
            {
                response = await InvokeWithRoutingAsync(
                    message, context, instructions, tools, sessionId, ct);
            }
            else
            {
                response = await InvokeStaticAsync(
                    message, context, instructions, tools, sessionId, ct);
            }
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
    /// Invokes the LLM through the intelligent routing pipeline (FR-1 through FR-8).
    /// </summary>
    private async Task<string> InvokeWithRoutingAsync(
        LeanKernelMessage message,
        ConversationContext context,
        string instructions,
        IReadOnlyList<AITool> tools,
        string sessionId,
        CancellationToken ct)
    {
        var (response, metadata) = await _routing!.RouteAsync(
            requestId: message.Id,
            prompt: message.Content,
            existingContextTokens: context.EstimatedTotalTokens,
            systemInstructions: instructions,
            tools: tools,
            ct: ct);

        // Persist routing metadata to session store for observability.
        await _sessions.SetMetadataAsync(sessionId, "routing:alias", metadata.SelectedAlias, ct);
        await _sessions.SetMetadataAsync(sessionId, "routing:tier", metadata.SelectedTier, ct);
        await _sessions.SetMetadataAsync(sessionId, "routing:complexity", metadata.Complexity.ToString(), ct);
        await _sessions.SetMetadataAsync(sessionId, "routing:cost_bucket", metadata.CostBucket, ct);
        await _sessions.SetMetadataAsync(sessionId, "routing:latency_ms", metadata.LatencyMs.ToString(), ct);
        await _sessions.SetMetadataAsync(sessionId, "routing:attempts", metadata.AttemptCount.ToString(), ct);

        return response;
    }

    /// <summary>
    /// Invokes the LLM via the static default model (phase 0 / routing disabled).
    /// </summary>
    private async Task<string> InvokeStaticAsync(
        LeanKernelMessage message,
        ConversationContext context,
        string instructions,
        IReadOnlyList<AITool> tools,
        string sessionId,
        CancellationToken ct)
    {
        var agent = _agentFactory.CreateAgent(instructions, tools);

        // Build history messages from gated context using SessionExtensions
        var messages = BuildMessages(context.History, message.Content);
        var agentSession = await agent.CreateSessionAsync(ct);
        var agentResponse = await agent.RunAsync(messages, agentSession, cancellationToken: ct);

        var response = agentResponse.Text ?? string.Empty;

        // Persist diagnostics metadata from middleware StateBag
        if (agentSession?.StateBag is { } bag)
        {
            // Known middleware keys set by DiagnosticsMiddleware
            string[] diagKeys = ["last_duration_ms", "last_message_count", "last_tool_calls"];
            foreach (var key in diagKeys)
            {
                var val = bag.GetValue<string>(key);
                if (val is not null)
                    await _sessions.SetMetadataAsync(sessionId, key, val, ct);
            }
        }

        return response;
    }

    /// <summary>
    /// Convert gated conversation history + current query into ChatMessage list.
    /// Uses <see cref="SessionExtensions"/> for ConversationTurn → ChatMessage mapping.
    /// The system message is handled by the agent's <c>instructions</c> parameter.
    /// </summary>
    internal static IEnumerable<ChatMessage> BuildMessages(
        IReadOnlyList<ConversationTurn> history,
        string currentQuery)
    {
        foreach (var msg in history.ToChatMessages())
            yield return msg;

        yield return new ChatMessage(ChatRole.User, currentQuery);
    }
}
