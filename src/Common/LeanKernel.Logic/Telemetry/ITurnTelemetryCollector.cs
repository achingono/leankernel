namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Collects telemetry for the current request-scoped assistant turn.
/// The chat client decorator calls <see cref="Capture"/> after each model invocation;
/// <see cref="Consume"/> is called by the persistence layer when storing the assistant turn.
/// </summary>
public interface ITurnTelemetryCollector
{
    /// <summary>
    /// Stores the captured telemetry for the current turn.
    /// </summary>
    /// <param name="telemetry">The telemetry to capture.</param>
    void Capture(TurnTelemetry telemetry);

    /// <summary>
    /// Returns and resets the captured telemetry for the current turn.
    /// Returns null if no telemetry was captured.
    /// </summary>
    /// <returns>The captured telemetry, or null if none.</returns>
    TurnTelemetry? Consume();
}