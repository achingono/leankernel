using System.Diagnostics.CodeAnalysis;

namespace LeanKernel.Logic.Tools.BuiltIn.Browser;

/// <summary>
/// Request body for submitting a browser automation run.
/// </summary>
public sealed record BrowserRunTaskRequest(
    string Task,
    string? StartUrl,
    string? Model,
    string? RequestKey,
    string? RequestId);

/// <summary>
/// Response returned when the browser sidecar accepts a run.
/// </summary>
public sealed record BrowserRunSubmissionResponse(
    string RunId,
    string Status,
    DateTimeOffset SubmittedAt,
    int? QueuePosition);

/// <summary>
/// Browser run status response returned by the sidecar.
/// </summary>
public sealed record BrowserRunStatusResponse(
    string RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? FinalDatum,
    IReadOnlyList<BrowserArtifactManifestItem> Artifacts,
    WebwrightError? Error);

/// <summary>
/// Describes one browser run artifact exposed by the sidecar manifest.
/// </summary>
public sealed record BrowserArtifactManifestItem(
    string Id,
    string Kind,
    string DisplayName,
    string ContentType,
    long Bytes);

/// <summary>
/// Browser artifact content downloaded from the sidecar.
/// </summary>
public sealed record BrowserArtifactContent(
    string RunId,
    string ArtifactId,
    string ContentType,
    byte[] Bytes,
    bool Truncated);

/// <summary>
/// Response returned when cancelling a browser run.
/// </summary>
public sealed record BrowserCancelRunResponse(
    string RunId,
    string Status,
    string Message);

/// <summary>
/// Sidecar error envelope.
/// </summary>
public sealed record WebwrightError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// Exception raised when the browser sidecar returns an error envelope.
/// </summary>
[SuppressMessage("Major Code Smell", "S3925", Justification = "Browser service exceptions are not serialized across process boundaries.")]
public sealed class WebwrightException : Exception
{
    public WebwrightException(
        string code,
        string message,
        int? statusCode = null,
        IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }

    public int? StatusCode { get; }

    public IReadOnlyDictionary<string, object?>? Details { get; }
}
