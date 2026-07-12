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

        var sessionGuid = await EnsureOwnedSessionAsync(
            scope,
            session,
            chatSessionId,
            conversationId,
            cancellationToken);

        AddRequestTurns(scope, context.RequestMessages, sessionGuid);
        AddResponseTurns(scope, context.ResponseMessages, sessionGuid);

        await scope.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> EnsureOwnedSessionAsync(
        EntityContext dbContext,
        AgentSession session,
        string? chatSessionId,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(chatSessionId)
            && Guid.TryParse(chatSessionId, out var existingSessionId))
        {
            await EnsureOwnershipAsync(dbContext, existingSessionId, cancellationToken);
            return existingSessionId;
        }

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
        dbContext.Sessions.Add(sessionEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
        session.StateBag.SetValue(ChatSessionIdKey, sessionEntity.Id.ToString());
        return sessionEntity.Id;
    }

    private async Task EnsureOwnershipAsync(EntityContext dbContext, Guid sessionGuid, CancellationToken cancellationToken)
    {
        var ownsSession = await dbContext.Sessions
            .AnyAsync(s =>
                s.Id == sessionGuid &&
                s.TenantId == permit.TenantId &&
                s.UserId == permit.UserId &&
                s.ChannelId == permit.ChannelId,
                cancellationToken);

        if (!ownsSession)
        {
            throw new InvalidOperationException(
                $"Session {sessionGuid} does not belong to the current identity " +
                $"(TenantId={permit.TenantId}, UserId={permit.UserId}, ChannelId={permit.ChannelId}).");
        }
    }

    private static void AddRequestTurns(EntityContext dbContext, IEnumerable<ChatMessage>? requestMessages, Guid sessionGuid)
    {
        if (requestMessages is null)
        {
            return;
        }

        foreach (var message in requestMessages.Where(ShouldPersistRequestMessage))
        {
            dbContext.Turns.Add(ToTurnEntity(message, sessionGuid, message.Role!.Value.ToString().ToLowerInvariant()));
        }
    }

    private static void AddResponseTurns(EntityContext dbContext, IEnumerable<ChatMessage>? responseMessages, Guid sessionGuid)
    {
        if (responseMessages is null)
        {
            return;
        }

        foreach (var message in responseMessages.Where(ShouldPersistResponseMessage))
        {
            dbContext.Turns.Add(ToTurnEntity(message, sessionGuid, "assistant"));
        }
    }

    private static bool ShouldPersistRequestMessage(ChatMessage message)
    {
        return (message.Role == ChatRole.User || message.Role == ChatRole.Tool)
            && !string.IsNullOrEmpty(message.Text);
    }

    private static bool ShouldPersistResponseMessage(ChatMessage message)
    {
        return message.Role == ChatRole.Assistant && !string.IsNullOrEmpty(message.Text);
    }

    private static TurnEntity ToTurnEntity(ChatMessage message, Guid sessionGuid, string role)
    {
        return new TurnEntity
        {
            SessionId = sessionGuid,
            Role = role,
            AuthorName = message.AuthorName,
            Content = message.Text!,
            Timestamp = message.CreatedAt ?? DateTimeOffset.UtcNow
        };
    }
}
