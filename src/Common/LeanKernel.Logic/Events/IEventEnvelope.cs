using LeanKernel;

namespace LeanKernel.Logic.Events;

/// <summary>
/// Non-generic base interface for event records that carry an <see cref="EventEnvelope"/>.
/// Used by <see cref="IEventSubscriber"/> for type-agnostic batch dispatch.
/// Extends <see cref="IHasEnvelope"/> to share the envelope contract with Core event types.
/// </summary>
public interface IEventEnvelope : IHasEnvelope
{
}
