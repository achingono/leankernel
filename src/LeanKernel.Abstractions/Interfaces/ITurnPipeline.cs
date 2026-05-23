using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface ITurnPipeline
{
    Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct = default);
}
