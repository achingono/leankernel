using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Async-flow accessor for current chat execution context.
/// </summary>
public sealed class ChatExecutionContextAccessor : IChatExecutionContextAccessor
{
    private static readonly AsyncLocal<ScopeHolder?> CurrentScope = new();

    /// <inheritdoc />
    public ChatExecutionContext? Current => CurrentScope.Value?.Context;

    /// <inheritdoc />
    public IDisposable BeginScope(ChatExecutionContext context)
    {
        var prior = CurrentScope.Value;
        CurrentScope.Value = new ScopeHolder(context);
        return new ScopeReset(prior);
    }

    private sealed class ScopeHolder
    {
        public ScopeHolder(ChatExecutionContext context) => Context = context;
        public ChatExecutionContext Context { get; }
    }

    private sealed class ScopeReset : IDisposable
    {
        private readonly ScopeHolder? _prior;
        private bool _disposed;

        public ScopeReset(ScopeHolder? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentScope.Value = _prior;
            _disposed = true;
        }
    }
}
