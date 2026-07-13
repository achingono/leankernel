namespace LeanKernel.Logic.Memory;

/// <summary>
/// Defines the small-model completion contract used by the memory pipeline.
/// </summary>
public interface IReasoningModel
{
    /// <summary>
    /// Gets a value indicating whether reasoning calls are currently enabled.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Requests a completion from the configured reasoning model.
    /// </summary>
    /// <param name="systemPrompt">The system prompt that constrains the model behavior.</param>
    /// <param name="userPrompt">The user prompt payload to complete.</param>
    /// <param name="maxOutputTokens">The maximum number of output tokens to request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The model response text, or <c>null</c> when reasoning is unavailable.</returns>
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken = default);
}
