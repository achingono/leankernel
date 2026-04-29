using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Middleware;

/// <summary>
/// IChatClient middleware that prunes messages before they reach the LLM,
/// enforcing LeanKernel's context budget. Applied at the chat client level
/// so it runs transparently for all agents using this client.
/// </summary>
public sealed class ContextGatingMiddleware
{
    private readonly ILogger<ContextGatingMiddleware> _logger;

    public ContextGatingMiddleware(ILogger<ContextGatingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Wrap a chat client with context gating that enforces token budgets.
    /// </summary>
    public IChatClient Wrap(IChatClient inner, ContextBudget budget)
    {
        return new ChatClientBuilder(inner)
            .Use(
                getResponseFunc: async (messages, options, innerClient, ct) =>
                {
                    var original = messages.ToList();
                    var pruned = PruneMessages(original, budget);

                    _logger.LogDebug(
                        "Context gating: {Original} → {Pruned} messages (budget={TotalTokens})",
                        original.Count, pruned.Count, budget.TotalTokens);

                    return await innerClient.GetResponseAsync(pruned, options, ct);
                },
                getStreamingResponseFunc: (messages, options, innerClient, ct) =>
                {
                    var original = messages.ToList();
                    var pruned = PruneMessages(original, budget);

                    return innerClient.GetStreamingResponseAsync(pruned, options, ct);
                })
            .Build();
    }

    /// <summary>
    /// Prune messages to fit within the conversation budget.
    /// Keeps the system message, the last N user/assistant turns that fit,
    /// and always preserves the most recent user message.
    /// </summary>
    internal static List<ChatMessage> PruneMessages(
        IReadOnlyList<ChatMessage> messages,
        ContextBudget budget)
    {
        if (messages.Count == 0)
            return [];

        var result = new List<ChatMessage>();

        // Always keep system messages
        foreach (var msg in messages.Where(m => m.Role == ChatRole.System))
            result.Add(msg);

        // Get non-system messages
        var conversationMessages = messages
            .Where(m => m.Role != ChatRole.System)
            .ToList();

        if (conversationMessages.Count == 0)
            return result;

        // Always keep the last message (current query)
        var lastMessage = conversationMessages[^1];

        // Estimate tokens: ~4 chars per token
        var usedTokens = result.Sum(m => EstimateTokens(m));
        var lastMessageTokens = EstimateTokens(lastMessage);
        var remainingBudget = budget.ConversationBudget - lastMessageTokens;

        // Add history messages from most recent backwards, within budget
        var historyToAdd = new List<ChatMessage>();
        for (int i = conversationMessages.Count - 2; i >= 0; i--)
        {
            var msg = conversationMessages[i];
            var tokens = EstimateTokens(msg);
            if (usedTokens + tokens <= remainingBudget)
            {
                historyToAdd.Add(msg);
                usedTokens += tokens;
            }
            else
            {
                break; // Budget exhausted, stop adding older messages
            }
        }

        // Reverse to maintain chronological order
        historyToAdd.Reverse();
        result.AddRange(historyToAdd);
        result.Add(lastMessage);

        return result;
    }

    private static int EstimateTokens(ChatMessage message) =>
        (message.Text?.Length ?? 0) / 4;
}
