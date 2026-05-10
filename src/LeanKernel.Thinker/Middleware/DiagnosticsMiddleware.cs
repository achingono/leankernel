using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LeanKernel.Thinker.Middleware;

/// <summary>
/// Agent Run middleware that captures diagnostics for the Blazor UI.
/// Records timing, message counts, and tool invocations in the session StateBag.
/// </summary>
public sealed class DiagnosticsMiddleware
{
    private readonly ILogger<DiagnosticsMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsMiddleware" /> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DiagnosticsMiddleware(ILogger<DiagnosticsMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Wrap an agent with diagnostics middleware that captures timing and message counts.
    /// </summary>
    public AIAgent Wrap(AIAgent agent)
    {
        return agent
            .AsBuilder()
            .Use(async (messages, session, options, next, ct) =>
            {
                var sw = Stopwatch.StartNew();
                var inputCount = messages.Count();

                await next(messages, session, options, ct);

                sw.Stop();

                if (session is not null)
                {
                    session.StateBag.SetValue("LeanKernel:last_duration_ms", sw.ElapsedMilliseconds.ToString());
                    session.StateBag.SetValue("LeanKernel:input_message_count", inputCount.ToString());
                    session.StateBag.SetValue("LeanKernel:timestamp", DateTimeOffset.UtcNow.ToString("o"));
                }

                _logger.LogDebug(
                    "Agent completed in {Duration}ms with {InputCount} input messages",
                    sw.ElapsedMilliseconds, inputCount);
            })
            .Build();
    }
}
