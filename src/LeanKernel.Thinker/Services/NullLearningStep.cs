using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Disabled learning step registered to keep pipeline shape explicit.
/// </summary>
public sealed class NullLearningStep : ILearningStep
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NullLearningStep" /> class.
    /// </summary>
    /// <param name="name">The disabled step name.</param>
    public NullLearningStep(string name)
    {
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct) =>
        Task.FromResult(LearningStepResult.Succeeded(Name, "Disabled by configuration."));
}
