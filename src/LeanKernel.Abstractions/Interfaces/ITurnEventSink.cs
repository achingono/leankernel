using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface ITurnEventSink
{
    Task PublishAsync(TurnEvent turnEvent, CancellationToken ct = default);
}
