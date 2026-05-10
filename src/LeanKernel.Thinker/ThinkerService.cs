using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Routing;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Thinker;

/// <summary>
/// Aggregates collaborators used by the thinker service.
/// </summary>
public sealed class ThinkerServiceDependencies
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThinkerServiceDependencies" /> class.
    /// </summary>
    /// <param name="gatekeeper">The service that selects context for a turn.</param>
    /// <param name="sessions">The session store used for conversation history.</param>
    /// <param name="wiki">The wiki store used by the thinker.</param>
    /// <param name="agentFactory">The factory that creates AI agents.</param>
    /// <param name="toolAdapter">The adapter that exposes tools to the AI agent.</param>
    /// <param name="promptAssembler">The service that builds system instructions.</param>
    /// <param name="responseEnhancer">The optional synchronous response enhancer.</param>
    /// <param name="turnEventSink">The optional sink for durable post-turn learning.</param>
    public ThinkerServiceDependencies(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IWikiStore wiki,
        AgentFactory agentFactory,
        ToolFunctionAdapter toolAdapter,
        PromptAssembler promptAssembler,
        IResponseEnhancer? responseEnhancer = null,
        ITurnEventSink? turnEventSink = null)
    {
        Gatekeeper = gatekeeper;
        Sessions = sessions;
        Wiki = wiki;
        AgentFactory = agentFactory;
        ToolAdapter = toolAdapter;
        PromptAssembler = promptAssembler;
        ResponseEnhancer = responseEnhancer;
        TurnEventSink = turnEventSink;
    }

    public IContextGatekeeper Gatekeeper { get; }
    public ISessionStore Sessions { get; }
    public IWikiStore Wiki { get; }
    public AgentFactory AgentFactory { get; }
    public ToolFunctionAdapter ToolAdapter { get; }
    public PromptAssembler PromptAssembler { get; }
    public IResponseEnhancer? ResponseEnhancer { get; }
    public ITurnEventSink? TurnEventSink { get; }
}

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
    private readonly SelectionLogStore? _selectionLog;
    private readonly IResponseEnhancer? _responseEnhancer;
    private readonly ITurnEventSink? _turnEventSink;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    public ThinkerService(
        ThinkerServiceDependencies dependencies,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger,
        ModelRoutingService? routing = null,
        SelectionLogStore? selectionLog = null)
    {
        _gatekeeper = dependencies.Gatekeeper;
        _sessions = dependencies.Sessions;
        _wiki = dependencies.Wiki;
        _agentFactory = dependencies.AgentFactory;
        _toolAdapter = dependencies.ToolAdapter;
        _promptAssembler = dependencies.PromptAssembler;
        _responseEnhancer = dependencies.ResponseEnhancer;
        _turnEventSink = dependencies.TurnEventSink;
        _routing = routing;
        _selectionLog = selectionLog;
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
        Exception? captureException = null;
        string? errorType = null;
        string? errorMessage = null;

        try
        {
            var instructions = _promptAssembler.AssembleSystemMessage(context);
            var tools = _toolAdapter.BuildTools();

            if (_config.Routing.Enabled && _routing is not null)
            {
                if (_config.Routing.ShadowMode)
                {
                    // Phase 1 — Shadow: run routing for logging only, return static response.
                    response = await InvokeStaticAsync(
                        message, context, instructions, tools, sessionId, ct);

                    _ = RunShadowRoutingAsync(message, context, instructions, tools, ct);
                }
                else
                {
                    response = await InvokeWithRoutingAsync(
                        message, context, instructions, tools, sessionId, ct);
                }
            }
            else
            {
                response = await InvokeStaticAsync(
                    message, context, instructions, tools, sessionId, ct);
            }
        }
        catch (Exception ex)
        {
            captureException = ex;
            errorType = ex.GetType().Name;
            errorMessage = ex.Message;
            _logger.LogError(ex, "LLM invocation failed");
            response = "I'm sorry, I encountered an error processing your request. Please try again.";
        }

        // 4a. Enhance response with knowledge synthesis (if enabled)
        if (_responseEnhancer is not null && captureException is null)
        {
            response = await _responseEnhancer.EnhanceResponseAsync(
                message.Content, response, context, ct);
        }

        // 5. Record outbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        var sourceId = $"conversation:{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss}";

        if (_turnEventSink is not null)
        {
            try
            {
                await _turnEventSink.EnqueueAsync(new TurnEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SessionId = sessionId,
                    UserMessage = message,
                    AssistantResponse = response,
                    Context = context,
                    SourceId = sourceId,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ErrorType = errorType,
                    ErrorMessage = errorMessage
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue turn event for self-improvement");
            }
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

        _selectionLog?.Record(metadata);

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
    /// Phase 1 shadow: fires routing in the background, records the SelectionResult for
    /// observability, but the caller has already returned the static response to the user.
    /// </summary>
    private async Task RunShadowRoutingAsync(
        LeanKernelMessage message,
        ConversationContext context,
        string instructions,
        IReadOnlyList<AITool> tools,
        CancellationToken ct)
    {
        try
        {
            var (_, metadata) = await _routing!.RouteAsync(
                requestId: message.Id,
                prompt: message.Content,
                existingContextTokens: context.EstimatedTotalTokens,
                systemInstructions: instructions,
                tools: tools,
                ct: ct);

            _selectionLog?.Record(metadata);

            _logger.LogInformation(
                "Shadow routing [{RequestId}]: would have selected alias='{Alias}' tier='{Tier}' " +
                "complexity={Complexity} cost={CostBucket} reason='{Reason}' attempts={Attempts} latency={LatencyMs}ms",
                metadata.RequestId, metadata.SelectedAlias, metadata.SelectedTier,
                metadata.Complexity, metadata.CostBucket, metadata.SelectionReason,
                metadata.AttemptCount, metadata.LatencyMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shadow routing [{RequestId}]: suppressed exception", message.Id);
        }
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
