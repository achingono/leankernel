using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(string toolName, IDictionary<string, object?> arguments, CancellationToken ct = default);
}
