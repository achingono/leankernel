using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

/// <summary>
/// Assembles and compacts conversation history for the context window.
/// </summary>
public sealed class ConversationHistoryAssembler
{
    private readonly ISessionStore _sessions;
    private readonly LeanKernelConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationHistoryAssembler" /> class.
    /// </summary>
    /// <param name="sessions">The session store used to load conversation history.</param>
    /// <param name="config">The LeanKernel configuration containing history limits.</param>
    public ConversationHistoryAssembler(ISessionStore sessions, IOptions<LeanKernelConfig> config)
    {
        _sessions = sessions;
        _config = config.Value;
    }

    /// <summary>
    /// Loads recent conversation history and compacts older turns.
    /// </summary>
    /// <param name="sessionId">The conversation session identifier.</param>
    /// <param name="ct">A token used to cancel history loading.</param>
    /// <returns>The assembled conversation turns.</returns>
    public async Task<List<ConversationTurn>> AssembleAsync(string sessionId, CancellationToken ct)
    {
        var allTurns = await _sessions.GetHistoryAsync(sessionId, ct);
        var maxTurns = Math.Min(allTurns.Count, _config.Context.MaxConversationTurns);
        var recentTurns = allTurns.TakeLast(maxTurns).ToList();
        var result = new List<ConversationTurn>();

        for (var i = 0; i < recentTurns.Count; i++)
        {
            var age = recentTurns.Count - i;
            var turn = recentTurns[i];

            if (age <= 3)
            {
                result.Add(turn);
            }
            else if (age <= 8)
            {
                result.Add(turn with
                {
                    Content = turn.Content.Length > 500 ? turn.Content[..500] + "..." : turn.Content,
                    IsCompacted = turn.Content.Length > 500
                });
            }
            else
            {
                result.Add(turn with
                {
                    Content = Truncate(turn.Content, 100),
                    IsCompacted = true
                });
            }
        }

        return result;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
