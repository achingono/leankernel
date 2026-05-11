using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Resources;
using LeanKernel.Thinker.Strategies;

namespace LeanKernel.Thinker;

/// <summary>
/// Main reasoning loop. Takes a message, obtains gated context,
/// invokes the selected model strategy, and publishes post-turn work.
/// </summary>
public sealed class ThinkerService : IThinkerService
{
    private readonly IContextGatekeeper _gatekeeper;
    private readonly ISessionStore _sessions;
    private readonly ToolFunctionAdapter _toolAdapter;
    private readonly PromptAssembler _promptAssembler;
    private readonly AgentStrategySelector? _strategySelector;
    private readonly IAgentStrategy _fallbackStrategy;
    private readonly IResponseEnhancer? _responseEnhancer;
    private readonly PostTurnPipeline? _postTurnPipeline;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThinkerService" /> class.
    /// </summary>
    /// <param name="dependencies">The grouped collaborators used by the turn pipeline.</param>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="logger">The logger used for turn diagnostics.</param>
    public ThinkerService(
        ThinkerServiceDependencies dependencies,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger)
    {
        _gatekeeper = dependencies.Gatekeeper;
        _sessions = dependencies.Sessions;
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

    /// <inheritdoc />
    public async Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct)
    {
        var sessionId = await _sessions.GetOrCreateSessionIdAsync(
            message.ChannelId, message.SenderId, ct);

        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "user",
            Content = message.Content,
            Timestamp = message.Timestamp
        }, ct);

        var budget = ContextBudget.FromModelWindow(_config.LiteLlm.ContextWindowTokens);
        var context = await _gatekeeper.GateContextAsync(message, budget, sessionId, ct);

        _logger.LogInformation(
            ResourceText.Log("ContextAssembled"),
            context.EstimatedTotalTokens, context.WikiLeanKernels.Count,
            context.History.Count, context.ExclusionLog.Count);

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
            _logger.LogError(ex, ResourceText.Error("LlmInvocationFailedLog"));
            response = ResourceText.Error("LlmInvocationFallbackResponse");
        }

        if (_responseEnhancer is not null)
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
    /// <param name="history">The gated conversation history.</param>
    /// <param name="currentQuery">The current inbound user query.</param>
    /// <returns>The chat messages sent to the model.</returns>
    internal static IEnumerable<ChatMessage> BuildMessages(
        IReadOnlyList<ConversationTurn> history,
        string currentQuery)
        => StaticAgentStrategy.BuildMessages(history, currentQuery);
}
