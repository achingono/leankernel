using System.Text.Json;

using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Gateway.Sessions;

/// <summary>
/// Durable agent session store backed by EF Core.
/// Uses <see cref="JsonSerializer"/> to persist session state.
/// Populates ownership metadata (TenantId, UserId, ChannelId) on <see cref="AgentStateEntity"/>.
/// </summary>
public class DbAgentStateStore(
    EntityContext entityContext,
    IPermit permit,
    ILogger<DbAgentStateStore> logger) : AgentSessionStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <inheritdoc />
    public override async ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await entityContext.AgentStates
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ScopedConversationId == conversationId, cancellationToken);

        if (entity is not null && !string.IsNullOrWhiteSpace(entity.StateJson))
        {
            try
            {
                var doc = JsonDocument.Parse(entity.StateJson);
                return JsonSerializer.Deserialize<ChatClientAgentSession>(doc, s_jsonOptions)
                    ?? await agent.CreateSessionAsync(cancellationToken);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize agent session state for conversation {ConversationId}; starting a new session.", conversationId);
                // Fall through to create new session if deserialization fails
            }
        }

        return await agent.CreateSessionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        string stateJson;

        if (session is ChatClientAgentSession typedSession)
        {
            var jsonElement = JsonSerializer.SerializeToElement(typedSession, s_jsonOptions);
            stateJson = jsonElement.GetRawText();
        }
        else
        {
            stateJson = "{}";
        }

        // Use a tracked query so EF manages the RowVersion concurrency token.
        var existing = await entityContext.AgentStates
            .FirstOrDefaultAsync(e => e.ScopedConversationId == conversationId, cancellationToken);

        if (existing is not null)
        {
            existing.StateJson = stateJson;
            existing.UpdatedOn = DateTimeOffset.UtcNow;
        }
        else
        {
            entityContext.AgentStates.Add(new AgentStateEntity
            {
                ScopedConversationId = conversationId,
                TenantId = permit.TenantId,
                UserId = permit.UserId,
                ChannelId = permit.ChannelId,
                StateJson = stateJson,
                CreatedOn = DateTimeOffset.UtcNow,
                UpdatedOn = DateTimeOffset.UtcNow
            });
        }

        try
        {
            await entityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Reload the winning state from the store and retry once (last-writer-wins).
            logger.LogWarning(ex, "Concurrency conflict saving agent state for {ConversationId}; reloading and overwriting.", conversationId);
            foreach (var entry in ex.Entries)
                await entry.ReloadAsync(cancellationToken);

            var conflicted = ex.Entries.FirstOrDefault()?.Entity as AgentStateEntity;
            if (conflicted is not null)
            {
                conflicted.StateJson = stateJson;
                conflicted.UpdatedOn = DateTimeOffset.UtcNow;
                await entityContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Duplicate key on concurrent insert: reload and update.
            entityContext.ChangeTracker.Clear();
            var conflicted = await entityContext.AgentStates
                .FirstOrDefaultAsync(e => e.ScopedConversationId == conversationId, cancellationToken);
            if (conflicted is not null)
            {
                conflicted.StateJson = stateJson;
                conflicted.UpdatedOn = DateTimeOffset.UtcNow;
                await entityContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}