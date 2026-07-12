using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides chat history storage backed by EF Core, filtered by tenant/user/channel identity.
/// Ownership is verified through <see cref="SessionEntity"/> to enforce partitioning.
/// </summary>
public class DbChatHistoryProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    IPermit permit) : ChatHistoryProvider
{
    internal const string ChatSessionIdKey = "chatSessionId";
    internal const string ConversationIdKey = "conversationId";

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("Session is required.");

        if (!session.StateBag.TryGetValue<string>(ChatSessionIdKey, out var chatSessionId)
            || string.IsNullOrEmpty(chatSessionId))
            return [];

        if (!Guid.TryParse(chatSessionId, out var chatSessionGuid))
            return [];

        using var scope = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Verify ownership: the session must belong to the current tenant/user/channel
        var ownsSession = await scope.Sessions
            .AnyAsync(s =>
                s.Id == chatSessionGuid &&
                s.TenantId == permit.TenantId &&
                s.UserId == permit.UserId &&
                s.ChannelId == permit.ChannelId,
                cancellationToken);

        if (!ownsSession)
            return [];

        var turns = await scope.Turns
            .Where(t => t.SessionId == chatSessionGuid)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(cancellationToken);

        return turns.Select(t => new ChatMessage
        {
            AuthorName = t.AuthorName,
            CreatedAt = t.Timestamp,
            Role = t.Role switch
            {
                "user" => ChatRole.User,
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            },
            Contents = [new TextContent(t.Content)]
        }).ToList();
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("Session is required.");
        session.StateBag.TryGetValue<string>(ChatSessionIdKey, out var chatSessionId);
        session.StateBag.TryGetValue<string>(ConversationIdKey, out var conversationId);

        using var scope = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Ensure the session entity exists with correct ownership
        if (string.IsNullOrEmpty(chatSessionId) || !Guid.TryParse(chatSessionId, out var sessionGuid))
        {
            var sessionEntity = new SessionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = permit.TenantId,
                UserId = permit.UserId,
                ChannelId = permit.ChannelId,
                ConversationId = conversationId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            scope.Sessions.Add(sessionEntity);
            await scope.SaveChangesAsync(cancellationToken);
            sessionGuid = sessionEntity.Id;
            chatSessionId = sessionEntity.Id.ToString();
            session.StateBag.SetValue(ChatSessionIdKey, chatSessionId);
        }
        else
        {
            // Verify ownership of existing session
            var ownsSession = await scope.Sessions
                .AnyAsync(s =>
                    s.Id == sessionGuid &&
                    s.TenantId == permit.TenantId &&
                    s.UserId == permit.UserId &&
                    s.ChannelId == permit.ChannelId,
                    cancellationToken);

            if (!ownsSession)
                throw new InvalidOperationException(
                    $"Session {sessionGuid} does not belong to the current identity " +
                    $"(TenantId={permit.TenantId}, UserId={permit.UserId}, ChannelId={permit.ChannelId}).");
        }

        // Store input request messages (user/tool)
        if (context.RequestMessages is not null)
        {
            foreach (var msg in context.RequestMessages)
            {
                if (msg.Role != ChatRole.User && msg.Role != ChatRole.Tool)
                    continue;
                if (string.IsNullOrEmpty(msg.Text))
                    continue;

                var turn = new TurnEntity
                {
                    SessionId = sessionGuid,
                    Role = msg.Role.Value.ToString().ToLowerInvariant(),
                    AuthorName = msg.AuthorName,
                    Content = msg.Text,
                    Timestamp = msg.CreatedAt ?? DateTimeOffset.UtcNow
                };
                scope.Turns.Add(turn);
            }
        }

        // Store response messages (assistant)
        if (context.ResponseMessages is not null)
        {
            foreach (var msg in context.ResponseMessages)
            {
                if (msg.Role != ChatRole.Assistant || string.IsNullOrEmpty(msg.Text))
                    continue;

                var turn = new TurnEntity
                {
                    SessionId = sessionGuid,
                    Role = "assistant",
                    AuthorName = msg.AuthorName,
                    Content = msg.Text,
                    Timestamp = msg.CreatedAt ?? DateTimeOffset.UtcNow
                };
                scope.Turns.Add(turn);
            }
        }

        await scope.SaveChangesAsync(cancellationToken);
    }
}
