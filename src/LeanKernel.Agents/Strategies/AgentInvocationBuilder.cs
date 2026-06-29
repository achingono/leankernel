using Microsoft.Extensions.AI;

namespace LeanKernel.Agents.Strategies;

/// <summary>
/// Provides functionality for agent invocation builder.
/// </summary>
internal static class AgentInvocationBuilder
{
    /// <summary>
    /// Builds messages.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public static IReadOnlyList<ChatMessage> BuildMessages(AgentStrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, context.SystemMessage)
        };

        foreach (var turn in context.History)
        {
            var role = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;

            messages.Add(new ChatMessage(role, turn.Content)
            {
                CreatedAt = turn.Timestamp
            });
        }

        messages.Add(new ChatMessage(ChatRole.User, context.UserMessage));
        return messages;
    }

    /// <summary>
    /// Builds options.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public static ChatOptions? BuildOptions(AgentStrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Tools is not { Count: > 0 })
        {
            return null;
        }

        return new ChatOptions
        {
            Tools = [.. context.Tools],
            ToolMode = ChatToolMode.Auto
        };
    }
}
