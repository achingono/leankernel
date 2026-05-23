using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents;

/// <summary>
/// Default agent runtime — public entry point that delegates to the turn pipeline.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime
{
    private readonly ITurnPipeline _pipeline;

    public AgentRuntime(ITurnPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    public Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default)
        => _pipeline.ProcessAsync(message, ct);
}
