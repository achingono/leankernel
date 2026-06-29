using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines the pipeline for processing conversation turns.
/// </summary>
public interface ITurnPipeline
{
    /// <summary>
    /// Processes a message through the turn pipeline.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous processing operation.</returns>
    Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct = default);
}
