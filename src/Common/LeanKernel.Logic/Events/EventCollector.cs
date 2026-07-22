namespace LeanKernel.Logic.Events;

using System.Collections.Concurrent;

using LeanKernel.Events;

/// <summary>
/// Thread-safe request-scoped event collector backed by a concurrent queue.
/// Events are emitted by providers and consumed by the event store at scope end.
/// </summary>
public sealed class EventCollector : IEventCollector
{
    private readonly ConcurrentQueue<object> _events = new();

    /// <inheritdoc />
    public void EmitTurn(TurnEvent turnEvent)
    {
        _events.Enqueue(turnEvent);
    }

    /// <inheritdoc />
    public void EmitToolCall(ToolCallEvent toolCallEvent)
    {
        _events.Enqueue(toolCallEvent);
    }

    /// <inheritdoc />
    public void EmitTelemetry(TelemetryEvent telemetryEvent)
    {
        _events.Enqueue(telemetryEvent);
    }

    /// <inheritdoc />
    public IReadOnlyList<object> ConsumeAll()
    {
        var snapshot = _events.ToArray();

        _events.Clear();

        return snapshot;
    }
}
