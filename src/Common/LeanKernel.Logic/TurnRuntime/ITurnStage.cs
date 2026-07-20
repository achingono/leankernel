namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// A single stage in the turn pipeline. Each stage processes the <see cref="TurnContext"/>
/// in order, mutating it as needed. Stages are executed sequentially by <see cref="TurnPipeline"/>.
/// </summary>
public interface ITurnStage
{
    /// <summary>
    /// A diagnostic name for this stage.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the stage against the turn context.
    /// </summary>
    /// <param name="context">The turn context to process.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the stage finishes.</returns>
    Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default);
}