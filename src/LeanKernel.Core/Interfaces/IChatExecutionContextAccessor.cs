using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Provides ambient chat turn context for tool execution defaults.
/// </summary>
public interface IChatExecutionContextAccessor
{
    /// <summary>
    /// Gets the current chat execution context if present.
    /// </summary>
    ChatExecutionContext? Current { get; }

    /// <summary>
    /// Pushes a context value for the current async execution flow.
    /// </summary>
    IDisposable BeginScope(ChatExecutionContext context);
}
