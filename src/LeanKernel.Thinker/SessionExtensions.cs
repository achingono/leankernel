using Microsoft.Extensions.AI;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker;

/// <summary>
/// Extension methods for mapping between LeanKernel's <see cref="ConversationTurn"/>
/// and MEAI's <see cref="ChatMessage"/>. Centralizes the conversion logic
/// used by ThinkerService and any future MAF session integration.
/// </summary>
public static class SessionExtensions
{
    /// <summary>
    /// Convert a <see cref="ConversationTurn"/> to a MEAI <see cref="ChatMessage"/>.
    /// </summary>
    public static ChatMessage ToChatMessage(this ConversationTurn turn)
    {
        var role = turn.Role switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };
        return new ChatMessage(role, turn.Content);
    }

    /// <summary>
    /// Convert a MEAI <see cref="ChatMessage"/> to a <see cref="ConversationTurn"/>.
    /// </summary>
    public static ConversationTurn ToConversationTurn(this ChatMessage message)
    {
        var role = message.Role == ChatRole.Assistant
            ? "assistant"
            : ResolveNonAssistantRole(message.Role);
        return new ConversationTurn
        {
            Role = role,
            Content = message.Text ?? string.Empty,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Convert a list of <see cref="ConversationTurn"/> to MEAI messages.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ToChatMessages(
        this IEnumerable<ConversationTurn> turns) =>
        turns.Select(t => t.ToChatMessage()).ToList();

    /// <summary>
    /// Convert MEAI messages to <see cref="ConversationTurn"/> list.
    /// </summary>
    public static IReadOnlyList<ConversationTurn> ToConversationTurns(
        this IEnumerable<ChatMessage> messages) =>
        messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => m.ToConversationTurn())
            .ToList();

    private static string ResolveNonAssistantRole(ChatRole role) =>
        role == ChatRole.System ? "system" : "user";
}
