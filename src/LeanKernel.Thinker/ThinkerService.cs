using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Services;
using LeanKernel.Thinker.Strategies;

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
    /// <param name="strategySelector">The selector that chooses the model invocation strategy.</param>
    /// <param name="responseEnhancer">The optional synchronous response enhancer.</param>
    /// <param name="postTurnPipeline">The pipeline that persists assistant output and publishes learning events.</param>
    public ThinkerServiceDependencies(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IWikiStore wiki,
        AgentFactory agentFactory,
        ToolFunctionAdapter toolAdapter,
        PromptAssembler promptAssembler,
        AgentStrategySelector? strategySelector = null,
        IResponseEnhancer? responseEnhancer = null,
        PostTurnPipeline? postTurnPipeline = null)
    {
        Gatekeeper = gatekeeper;
        Sessions = sessions;
        Wiki = wiki;
        AgentFactory = agentFactory;
        ToolAdapter = toolAdapter;
        PromptAssembler = promptAssembler;
        StrategySelector = strategySelector;
        ResponseEnhancer = responseEnhancer;
        PostTurnPipeline = postTurnPipeline;
    }

    public IContextGatekeeper Gatekeeper { get; }
    public ISessionStore Sessions { get; }
    public IWikiStore Wiki { get; }
    public AgentFactory AgentFactory { get; }
    public ToolFunctionAdapter ToolAdapter { get; }
    public PromptAssembler PromptAssembler { get; }
    public AgentStrategySelector? StrategySelector { get; }
    public IResponseEnhancer? ResponseEnhancer { get; }
    public PostTurnPipeline? PostTurnPipeline { get; }
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
    private readonly ToolFunctionAdapter _toolAdapter;
    private readonly PromptAssembler _promptAssembler;
    private readonly AgentStrategySelector? _strategySelector;
    private readonly IAgentStrategy _fallbackStrategy;
    private readonly IResponseEnhancer? _responseEnhancer;
    private readonly PostTurnPipeline? _postTurnPipeline;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    public ThinkerService(
        ThinkerServiceDependencies dependencies,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger)
    {
        _gatekeeper = dependencies.Gatekeeper;
        _sessions = dependencies.Sessions;
        _wiki = dependencies.Wiki;
        _toolAdapter = dependencies.ToolAdapter;
        _promptAssembler = dependencies.PromptAssembler;
        _strategySelector = dependencies.StrategySelector;
        _fallbackStrategy = new StaticAgentStrategy(dependencies.AgentFactory, _sessions);
        _responseEnhancer = dependencies.ResponseEnhancer;
        _postTurnPipeline = dependencies.PostTurnPipeline
            ?? new PostTurnPipeline(_sessions, NullLogger<PostTurnPipeline>.Instance);
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

            var strategy = _strategySelector?.Select() ?? _fallbackStrategy;
            response = await strategy.InvokeAsync(
                new AgentStrategyContext(message, context, instructions, tools, sessionId),
                ct);
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

        if (_postTurnPipeline is not null)
        {
            await _postTurnPipeline.CompleteAsync(
                sessionId,
                message,
                response,
                context,
                errorType,
                errorMessage,
                ct);
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
        => StaticAgentStrategy.BuildMessages(history, currentQuery);
}
