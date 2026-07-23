namespace LeanKernel;

/// <summary>
/// Marker interface for event records that carry an <see cref="EventEnvelope"/>.
/// Enables generic envelope resolution in <c>DbEventStore</c> without a closed switch.
/// </summary>
public interface IHasEnvelope
{
    /// <summary>
    /// Gets the event envelope containing partitioning, correlation, and versioning metadata.
    /// </summary>
    EventEnvelope Envelope { get; }
}
