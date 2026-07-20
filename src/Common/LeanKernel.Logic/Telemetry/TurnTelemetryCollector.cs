namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Scoped service that stores one <see cref="TurnTelemetry"/> per request.
/// <see cref="Capture"/> is called by the chat client decorator;
/// <see cref="Consume"/> is called by the persistence layer when storing the assistant turn.
/// </summary>
public sealed class TurnTelemetryCollector : ITurnTelemetryCollector
{
    private TurnTelemetry? _current;

    /// <inheritdoc />
    public void Capture(TurnTelemetry telemetry)
    {
        _current = telemetry;
    }

    /// <inheritdoc />
    public TurnTelemetry? Consume()
    {
        var captured = _current;
        _current = null;
        return captured;
    }
}