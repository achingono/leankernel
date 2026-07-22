namespace LeanKernel.Logic.Events;

using System.Text.Json;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Events;

/// <summary>
/// Persists event spine records durably in the EntityContext database.
/// </summary>
public sealed class DbEventStore : IEventStore
{
    private readonly EntityContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbEventStore"/> class.
    /// </summary>
    /// <param name="context">The scoped entity context.</param>
    public DbEventStore(EntityContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AppendAsync(object eventRecord, CancellationToken cancellationToken = default)
    {
        _context.Events.Add(ToEntity(eventRecord));
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AppendBatchAsync(IEnumerable<object> eventRecords, CancellationToken cancellationToken = default)
    {
        var entities = eventRecords
            .Select(ToEntity)
            .ToList();

        if (entities.Count == 0)
        {
            return;
        }

        _context.Events.AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static EventEntity ToEntity(object eventRecord)
    {
        var envelope = ResolveEnvelope(eventRecord);

        return new EventEntity
        {
            Id = Guid.NewGuid(),
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            SchemaVersion = envelope.SchemaVersion,
            TenantId = envelope.TenantId,
            PersonId = envelope.PersonId,
            UserId = envelope.UserId,
            ChannelId = envelope.ChannelId,
            SessionId = envelope.SessionId,
            Timestamp = envelope.Timestamp,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            RecordType = eventRecord.GetType().FullName ?? eventRecord.GetType().Name,
            PayloadJson = JsonSerializer.Serialize(eventRecord, eventRecord.GetType(), Constants.Serialization.JsonOptions),
            CreatedOn = DateTime.UtcNow,
        };
    }

    private static EventEnvelope ResolveEnvelope(object eventRecord)
    {
        return eventRecord switch
        {
            TurnEvent turn => turn.Envelope,
            ToolCallEvent toolCall => toolCall.Envelope,
            TelemetryEvent telemetry => telemetry.Envelope,
            _ => throw new InvalidOperationException(
                $"Unsupported event record type '{eventRecord.GetType().FullName}' for durable event storage."),
        };
    }
}
