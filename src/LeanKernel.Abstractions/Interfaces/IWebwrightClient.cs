using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Client abstraction for LeanKernel's browser automation sidecar.
/// </summary>
public interface IWebwrightClient
{
    /// <summary>
    /// Submits a browser automation run.
    /// </summary>
    /// <param name="request">The browser run request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The accepted run response.</returns>
    Task<BrowserRunSubmissionResponse> SubmitRunAsync(BrowserRunTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Retrieves browser run status and artifact manifest.
    /// </summary>
    /// <param name="runId">The browser run identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The current run status.</returns>
    Task<BrowserRunStatusResponse> GetRunAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Downloads one manifest-listed browser run artifact.
    /// </summary>
    /// <param name="runId">The browser run identifier.</param>
    /// <param name="artifactId">The opaque artifact identifier.</param>
    /// <param name="maxBytes">The maximum bytes to return.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The downloaded artifact content.</returns>
    Task<BrowserArtifactContent> GetArtifactAsync(string runId, string artifactId, int maxBytes, CancellationToken ct = default);

    /// <summary>
    /// Requests cancellation of a browser automation run.
    /// </summary>
    /// <param name="runId">The browser run identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The cancellation response.</returns>
    Task<BrowserCancelRunResponse> CancelRunAsync(string runId, CancellationToken ct = default);
}
