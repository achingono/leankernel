using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Main reasoning loop. Takes a message, obtains gated context
/// from the Archivist, sends to LLM via Semantic Kernel, and
/// persists learned facts back to the wiki.
/// </summary>
public sealed class ThinkerService : IThinkerService
{
    private readonly IContextGatekeeper _gatekeeper;
    private readonly ISessionStore _sessions;
    private readonly IWikiStore _wiki;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    public ThinkerService(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IWikiStore wiki,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger)
    {
        _gatekeeper = gatekeeper;
        _sessions = sessions;
        _wiki = wiki;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct)
    {
        // 1. Resolve session
        var sessionId = await _sessions.GetOrCreateSessionIdAsync(
            message.ChannelId, message.SenderId, ct);

        // 2. Record inbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "user",
            Content = message.Content,
            Timestamp = message.Timestamp
        }, ct);

        // 3. Gate context (deny-by-default)
        var budget = ContextBudget.FromModelWindow(_config.LiteLlm.ContextWindowTokens);
        var context = await _gatekeeper.GateContextAsync(message, budget, sessionId, ct);

        _logger.LogInformation(
            "Context assembled: ~{Tokens} tokens, {Excluded} exclusions",
            context.EstimatedTotalTokens, context.ExclusionLog.Count);

        // 4. Call LLM via Semantic Kernel
        // TODO: Phase 2 — wire up SK kernel invocation
        var response = $"[LeanKernel stub] Received: \"{Truncate(message.Content, 60)}\" — " +
                       $"Context: {context.WikiLeanKernels.Count} wiki LeanKernels, " +
                       $"{context.History.Count} history turns, " +
                       $"~{context.EstimatedTotalTokens} tokens";

        // 5. Record outbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // 6. Extract and persist 5W1H facts from the exchange
        // TODO: Phase 2 — WikiExtractor to extract facts from response

        return response;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}
