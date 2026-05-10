using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;
using LeanKernel.Thinker.Services;
using LeanKernel.Thinker.Strategies;

namespace LeanKernel.Thinker;

/// <summary>
/// Aggregates collaborators required by <see cref="ThinkerService" />.
/// </summary>
public sealed class ThinkerServiceDependencies
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThinkerServiceDependencies" /> class.
    /// </summary>
    /// <param name="gatekeeper">The service that selects context for a turn.</param>
    /// <param name="sessions">The session store used for conversation history.</param>
    /// <param name="wiki">The wiki store used by compatibility construction paths.</param>
    /// <param name="agentFactory">The factory that creates AI agents.</param>
    /// <param name="toolAdapter">The adapter that exposes tools to the AI agent.</param>
    /// <param name="promptAssembler">The service that builds system instructions.</param>
    /// <param name="strategySelector">The optional selector that chooses the model invocation strategy.</param>
    /// <param name="responseEnhancer">The optional synchronous response enhancer.</param>
    /// <param name="postTurnPipeline">The optional pipeline that persists assistant output and publishes learning events.</param>
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

    /// <summary>
    /// Gets the service that builds a lean context window for a turn.
    /// </summary>
    public IContextGatekeeper Gatekeeper { get; }

    /// <summary>
    /// Gets the store used to persist and retrieve session turns.
    /// </summary>
    public ISessionStore Sessions { get; }

    /// <summary>
    /// Gets the wiki store retained for compatibility with existing construction paths.
    /// </summary>
    public IWikiStore Wiki { get; }

    /// <summary>
    /// Gets the factory that creates AI agents.
    /// </summary>
    public AgentFactory AgentFactory { get; }

    /// <summary>
    /// Gets the adapter that exposes tools as callable model functions.
    /// </summary>
    public ToolFunctionAdapter ToolAdapter { get; }

    /// <summary>
    /// Gets the prompt assembler used to prepare system instructions.
    /// </summary>
    public PromptAssembler PromptAssembler { get; }

    /// <summary>
    /// Gets the optional selector for routed or shadow-routed model invocation.
    /// </summary>
    public AgentStrategySelector? StrategySelector { get; }

    /// <summary>
    /// Gets the optional synchronous response enhancement pipeline.
    /// </summary>
    public IResponseEnhancer? ResponseEnhancer { get; }

    /// <summary>
    /// Gets the optional post-turn persistence and self-improvement pipeline.
    /// </summary>
    public PostTurnPipeline? PostTurnPipeline { get; }
}
