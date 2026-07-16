namespace LeanKernel.Logic.Tools.BuiltIn.Browser;

/// <summary>
/// Client abstraction for LeanKernel's browser automation sidecar.
/// </summary>
public interface IWebwrightClient
{
    /// <summary>
    /// Submits a browser automation run.
    /// </summary>
    Task<BrowserRunSubmissionResponse> SubmitRunAsync(BrowserRunTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Retrieves browser run status and artifact manifest.
    /// </summary>
    Task<BrowserRunStatusResponse> GetRunAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Downloads one manifest-listed browser run artifact.
    /// </summary>
    Task<BrowserArtifactContent> GetArtifactAsync(string runId, string artifactId, int maxBytes, CancellationToken ct = default);

    /// <summary>
    /// Requests cancellation of a browser automation run.
    /// </summary>
    Task<BrowserCancelRunResponse> CancelRunAsync(string runId, CancellationToken ct = default);
}
