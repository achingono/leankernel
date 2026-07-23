namespace LeanKernel.Logic.Events;

using LeanKernel.Events;

/// <summary>
/// Request-scoped collector for append-only events.
/// Events are accumulated during a request and consumed by the event store
/// or projection layer at the end of the request scope.
/// </summary>
public interface IEventCollector
{
    /// <summary>
    /// Emits a turn event.
    /// </summary>
    /// <param name="turnEvent">The turn event to emit.</param>
    void EmitTurn(TurnEvent turnEvent);

    /// <summary>
    /// Emits a tool call event.
    /// </summary>
    /// <param name="toolCallEvent">The tool call event to emit.</param>
    void EmitToolCall(ToolCallEvent toolCallEvent);

    /// <summary>
    /// Emits a telemetry event.
    /// </summary>
    /// <param name="telemetryEvent">The telemetry event to emit.</param>
    void EmitTelemetry(TelemetryEvent telemetryEvent);

    /// <summary>
    /// Emits any event type via generic method. Per-type methods delegate to this internally.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="event">The event to emit.</param>
    void Emit<T>(T @event);

    /// <summary>
    /// Returns and clears all accumulated events for batch persistence.
    /// </summary>
    /// <returns>A snapshot of all accumulated events.</returns>
    IReadOnlyList<object> ConsumeAll();
}