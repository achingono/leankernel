using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IAgentRuntime
{
    Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default);
}
