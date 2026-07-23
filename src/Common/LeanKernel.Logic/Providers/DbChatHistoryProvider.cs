using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Events;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Telemetry;

using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides chat history storage backed by EF Core, filtered by tenant/user/channel identity.
/// Ownership is verified through <see cref="SessionEntity"/> to enforce partitioning.
/// All user data access uses <see cref="IRepository{TEntity}"/> to enforce permits and filters.
/// </summary>
public class DbChatHistoryProvider(
    IRepository<SessionEntity> sessionRepo,
    IRepository<TurnEntity> turnRepo,
    IRepository<TurnTelemetryEntity> telemetryRepo,
    IPermit permit,
    ITurnTelemetryCollector? telemetryCollector = null,
    IEventCollector? eventCollector = null,
    IEventStore? eventStore = null,
    ILogger<DbChatHistoryProvider>? logger = null) : ChatHistoryProvider
{
    internal const string ChatSessionIdKey = "chatSessionId";
    internal const string ConversationIdKey = "conversationId";

    /// <summary>
    /// Maximum number of recent turns retrieved per session to bound context growth.
    /// </summary>
    internal const int RecentTurnWindow = 200;

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("Session is required.");

        if (!session.StateBag.TryGetValue<string>(ChatSessionIdKey, out var chatSessionId)
            || string.IsNullOrEmpty(chatSessionId))
        {
            return [];
        }

        if (!Guid.TryParse(chatSessionId, out var chatSessionGuid))
        {
            return [];
        }

        var ownsSession = await sessionRepo.GetAll()
            .AnyAsync(
                s =>
                s.Id == chatSessionGuid &&
                s.TenantId == permit.TenantId &&
                s.UserId == permit.UserId &&
                s.ChannelId == permit.ChannelId,
                cancellationToken);

        if (!ownsSession)
        {
            return [];
        }

        var turns = await turnRepo.GetAll()
            .Where(t => t.SessionId == chatSessionGuid)
            .ToListAsync(cancellationToken);

        // Order and bound in memory — DB-side DateTimeOffset ordering is provider-dependent.
        var recentTurns = turns
            .OrderByDescending(t => t.Timestamp)
            .Take(RecentTurnWindow)
            .ToList();

        if (recentTurns.Count == RecentTurnWindow)
        {
            logger?.LogDebug("Chat history truncated to {Window} turns for session {SessionId}", RecentTurnWindow, chatSessionGuid);
        }

        return recentTurns.OrderBy(t => t.Timestamp)
            .Select(MapTurnToMessage)
            .Where(m => m is not null)
            .Cast<ChatMessage>()
            .ToList();
    }

    /// <inheritdoc />
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException("Session is required.");
        try
        {
            session.StateBag.TryGetValue<string>(ChatSessionIdKey, out var chatSessionId);
            session.StateBag.TryGetValue<string>(ConversationIdKey, out var conversationId);

            var sessionGuid = await EnsureOwnedSessionAsync(
                session,
                chatSessionId,
                conversationId,
                cancellationToken);

            // Collect candidate turns and assign idempotency keys before checking for duplicates.
            var requestTurns = BuildTurnEntities(context.RequestMessages, sessionGuid, isRequest: true);
            var responseTurns = BuildTurnEntities(context.ResponseMessages, sessionGuid, isRequest: false);
            var allCandidateTurns = requestTurns.Concat(responseTurns).ToList();

            if (allCandidateTurns.Count == 0)
            {
                return;
            }

            // Load existing idempotency keys for this session to detect retries.
            var existingKeys = new HashSet<string>(
                await turnRepo.GetAll()
                    .Where(t => t.SessionId == sessionGuid && t.Metadata != null)
                    .Select(t => t.Metadata!)
                    .ToListAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase);

            // Consume telemetry captured for the assistant turn (if any).
            var telemetry = telemetryCollector?.Consume();

            foreach (var turn in allCandidateTurns)
            {
                if (!existingKeys.Contains(turn.Metadata!))
                {
                    turnRepo.Add(turn);
                    existingKeys.Add(turn.Metadata!);

                    // Emit turn event alongside persistence for the event spine.
                    EmitTurnEvent(turn, sessionGuid);

                    // Persist telemetry for assistant turns when available.
                    if (telemetry is not null && turn.Role == "assistant")
                    {
                        var telemetryEntity = new TurnTelemetryEntity
                        {
                            TurnId = turn.Id,
                            RequestedModel = telemetry.RequestedModel,
                            ServedModel = telemetry.ServedModel,
                            Provider = telemetry.Provider,
                            ModelId = telemetry.ModelId,
                            ApiBase = telemetry.ApiBase,
                            PromptTokens = telemetry.PromptTokens,
                            CompletionTokens = telemetry.CompletionTokens,
                            TotalTokens = telemetry.TotalTokens,
                            ResponseCost = telemetry.ResponseCost,
                            Currency = telemetry.Currency,
                            CostIsEstimated = telemetry.CostIsEstimated,
                            LatencyMs = telemetry.Latency.HasValue
                                ? (long)telemetry.Latency.Value.TotalMilliseconds
                                : null,
                            CapturedAt = telemetry.CapturedAt,
                            SchemaVersion = telemetry.SchemaVersion,
                            CreatedOn = DateTime.UtcNow,
                            CreatedBy = new Badge
                            {
                                Id = Guid.Empty,
                                FullName = "System",
                                Email = string.Empty
                            }
                        };

                        telemetryRepo.Add(telemetryEntity);

                        // Emit telemetry event alongside persistence.
                        EmitTelemetryEvent(telemetryEntity, telemetry);
                    }
                }
            }

            await turnRepo.SaveChangesAsync(cancellationToken);

            if (eventCollector is not null && eventStore is not null)
            {
                var pendingEvents = eventCollector.ConsumeAll();
                if (pendingEvents.Count > 0)
                {
                    await eventStore.AppendBatchAsync(pendingEvents, cancellationToken);
                }
            }
        }
        catch (InvalidOperationException ex) when (IsPermitFailure(ex))
        {
            logger?.LogWarning(
                ex,
                "Chat history persistence skipped because operation was not permitted for current identity (TenantId={TenantId}, UserId={UserId}, ChannelId={ChannelId}).",
                permit.TenantId,
                permit.UserId,
                permit.ChannelId);
        }
    }

    private static bool IsPermitFailure(InvalidOperationException exception)
    {
        return exception.Message.Contains("not permitted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the current request owns the chat session, creating it when needed.
    /// </summary>
    private async Task<Guid> EnsureOwnedSessionAsync(
        AgentSession session,
        string? chatSessionId,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(chatSessionId)
            && Guid.TryParse(chatSessionId, out var existingSessionId))
        {
            await EnsureOwnershipAsync(existingSessionId, cancellationToken);
            return existingSessionId;
        }

        var sessionEntity = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            ConversationId = conversationId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge
            {
                Id = permit.UserId,
                FullName = "System",
                Email = string.Empty
            }
        };
        sessionRepo.Add(sessionEntity);
        await sessionRepo.SaveChangesAsync(cancellationToken);
        session.StateBag.SetValue(ChatSessionIdKey, sessionEntity.Id.ToString());
        return sessionEntity.Id;
    }

    /// <summary>
    /// Verifies that a session belongs to the current tenant, user, and channel partition.
    /// </summary>
    private async Task EnsureOwnershipAsync(Guid sessionGuid, CancellationToken cancellationToken)
    {
        var ownsSession = await sessionRepo.GetAll()
            .AnyAsync(
                s =>
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

    /// <summary>
    /// Builds turn entities from chat messages, filtering by persistence rules and computing idempotency keys.
    /// </summary>
    private static List<TurnEntity> BuildTurnEntities(
        IEnumerable<ChatMessage>? messages,
        Guid sessionGuid,
        bool isRequest)
    {
        if (messages is null)
        {
            return [];
        }

        var filter = isRequest
            ? (Func<ChatMessage, bool>)ShouldPersistRequestMessage
            : ShouldPersistResponseMessage;

        return messages
            .Where(filter)
            .Select(m => ToTurnEntity(m, sessionGuid, isRequest ? m.Role!.Value.ToString().ToLowerInvariant() : "assistant"))
            .ToList();
    }

    /// <summary>
    /// Determines whether a request message should be stored as a turn.
    /// </summary>
    private static bool ShouldPersistRequestMessage(ChatMessage message)
    {
        return (message.Role == ChatRole.User || message.Role == ChatRole.Tool)
            && !string.IsNullOrEmpty(message.Text);
    }

    /// <summary>
    /// Determines whether a response message should be stored as a turn.
    /// </summary>
    private static bool ShouldPersistResponseMessage(ChatMessage message)
    {
        return message.Role == ChatRole.Assistant && !string.IsNullOrEmpty(message.Text);
    }

    /// <summary>
    /// Converts a chat message into a persisted turn entity with an idempotency key in Metadata.
    /// </summary>
    private static TurnEntity ToTurnEntity(ChatMessage message, Guid sessionGuid, string role)
    {
        var turn = new TurnEntity
        {
            SessionId = sessionGuid,
            Role = role,
            AuthorName = message.AuthorName,
            Content = message.Text!,
            Timestamp = message.CreatedAt ?? DateTimeOffset.UtcNow,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge
            {
                Id = Guid.Empty,
                FullName = "System",
                Email = string.Empty
            }
        };

        turn.Metadata = turn.ComputeIdempotencyKey();
        return turn;
    }

    /// <summary>
    /// Converts a persisted turn entity to a chat message, preserving role semantics.
    /// Returns null for unrecognized roles so they are skipped rather than corrupting message provenance.
    /// </summary>
    private static ChatMessage? MapTurnToMessage(TurnEntity t)
    {
        ChatRole? role = t.Role switch
        {
            "user" => ChatRole.User,
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => null
        };

        if (role is null)
        {
            return null;
        }

        return new ChatMessage
        {
            AuthorName = t.AuthorName,
            CreatedAt = t.Timestamp,
            Role = role.Value,
            Contents = [new TextContent(t.Content)]
        };
    }

    /// <summary>
    /// Emits a turn event to the event collector alongside current persistence.
    /// </summary>
    private void EmitTurnEvent(TurnEntity turn, Guid sessionGuid)
    {
        if (eventCollector is null)
        {
            return;
        }

        var envelope = new EventEnvelope
        {
            EventType = "turn",
            TenantId = permit.TenantId,
            PersonId = permit.PersonId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
            SessionId = sessionGuid.ToString(),
            CorrelationId = turn.Metadata,
        };

        eventCollector.EmitTurn(new TurnEvent
        {
            Envelope = envelope,
            Role = turn.Role,
            Content = turn.Content,
            AuthorName = turn.AuthorName,
            SessionId = sessionGuid.ToString(),
            IdempotencyKey = turn.Metadata,
        });
    }

    /// <summary>
    /// Emits a telemetry event to the event collector alongside current persistence.
    /// </summary>
    private void EmitTelemetryEvent(TurnTelemetryEntity entity, TurnTelemetry telemetry)
    {
        if (eventCollector is null)
        {
            return;
        }

        var envelope = new EventEnvelope
        {
            EventType = "telemetry",
            TenantId = permit.TenantId,
            PersonId = permit.PersonId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
        };

        eventCollector.EmitTelemetry(new TelemetryEvent
        {
            Envelope = envelope,
            RequestedModel = entity.RequestedModel,
            ServedModel = entity.ServedModel,
            Provider = entity.Provider,
            ModelId = entity.ModelId,
            ApiBase = entity.ApiBase,
            PromptTokens = entity.PromptTokens,
            CompletionTokens = entity.CompletionTokens,
            TotalTokens = entity.TotalTokens,
            ResponseCost = entity.ResponseCost,
            Currency = entity.Currency,
            CostIsEstimated = entity.CostIsEstimated,
            LatencyMs = entity.LatencyMs,
            CapturedAt = entity.CapturedAt,
        });
    }
}
