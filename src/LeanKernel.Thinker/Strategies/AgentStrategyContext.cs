using Microsoft.Extensions.AI;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Carries all data required for a model invocation strategy to run one turn.
/// </summary>
/// <param name="Message">The inbound user message.</param>
/// <param name="Context">The gated conversation context.</param>
/// <param name="Instructions">The assembled system instructions.</param>
/// <param name="Tools">The tools available to the agent.</param>
/// <param name="SessionId">The persisted conversation session identifier.</param>
public sealed record AgentStrategyContext(
    LeanKernelMessage Message,
    ConversationContext Context,
    string Instructions,
    IReadOnlyList<AITool> Tools,
    string SessionId);
