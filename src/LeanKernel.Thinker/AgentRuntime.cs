using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Default agent runtime that routes turns through the primary Thinker loop.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime
{
    private readonly IThinkerService _thinker;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRuntime" /> class.
    /// </summary>
    /// <param name="thinker">The Thinker service that executes the canonical turn pipeline.</param>
    public AgentRuntime(IThinkerService thinker)
    {
        _thinker = thinker;
    }

    /// <inheritdoc />
    public Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct)
        => _thinker.ProcessAsync(message, ct);
}
