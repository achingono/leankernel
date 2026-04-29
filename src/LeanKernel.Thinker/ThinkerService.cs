using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.SemanticKernel;

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
    private readonly KernelFactory _kernelFactory;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ThinkerService> _logger;

    public ThinkerService(
        IContextGatekeeper gatekeeper,
        ISessionStore sessions,
        IWikiStore wiki,
        KernelFactory kernelFactory,
        IOptions<LeanKernelConfig> config,
        ILogger<ThinkerService> logger)
    {
        _gatekeeper = gatekeeper;
        _sessions = sessions;
        _wiki = wiki;
        _kernelFactory = kernelFactory;
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
            "Context assembled: ~{Tokens} tokens, {Wiki} wiki, {Turns} turns, {Excluded} exclusions",
            context.EstimatedTotalTokens, context.WikiLeanKernels.Count,
            context.History.Count, context.ExclusionLog.Count);

        // 4. Call LLM via Semantic Kernel
        string response;
        try
        {
            var kernel = _kernelFactory.Build();
            response = await LiteLlmConnector.InvokeAsync(kernel, context, message.Content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM invocation failed — returning error message");
            response = "I'm sorry, I encountered an error processing your request. Please try again.";
        }

        // 5. Record outbound turn
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // 6. Extract and persist 5W1H facts from the exchange
        try
        {
            var sourceId = $"conversation:{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss}";
            var facts = WikiExtractor.ExtractFacts(message.Content, response, sourceId);
            if (facts.Count > 0)
            {
                await _wiki.IngestFactsAsync(facts, ct);
                _logger.LogDebug("Extracted {Count} wiki entries from conversation", facts.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction failed — continuing without persistence");
        }

        return response;
    }
}
