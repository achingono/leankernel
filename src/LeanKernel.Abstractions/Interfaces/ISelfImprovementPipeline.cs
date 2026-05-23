using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface ISelfImprovementPipeline
{
    Task ProcessTurnEventAsync(TurnEvent turnEvent, CancellationToken ct = default);
}
