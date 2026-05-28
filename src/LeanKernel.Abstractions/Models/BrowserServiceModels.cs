using System.Diagnostics.CodeAnalysis;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Request body for submitting a browser automation run.
/// </summary>
/// <param name="Task">The natural-language browser task.</param>
/// <param name="StartUrl">The optional absolute HTTP or HTTPS start URL.</param>
/// <param name="Model">The optional LiteLLM model alias override.</param>
/// <param name="RequestKey">The optional concurrency serialization key.</param>
/// <param name="RequestId">The optional idempotency key.</param>
public sealed record BrowserRunTaskRequest(
    string Task,
    string? StartUrl,
    string? Model,
    string? RequestKey,
    string? RequestId);

/// <summary>
/// Response returned when the browser sidecar accepts a run.
/// </summary>
/// <param name="RunId">The accepted run identifier.</param>
/// <param name="Status">The initial run status.</param>
/// <param name="SubmittedAt">The submission timestamp.</param>
/// <param name="QueuePosition">The optional queue position.</param>
public sealed record BrowserRunSubmissionResponse(
    string RunId,
    string Status,
    DateTimeOffset SubmittedAt,
    int? QueuePosition);

/// <summary>
/// Browser run status response returned by the sidecar.
/// </summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Status">The current run status.</param>
/// <param name="StartedAt">The optional start timestamp.</param>
/// <param name="CompletedAt">The optional completion timestamp.</param>
/// <param name="ExitCode">The optional Webwright process exit code.</param>
/// <param name="FinalDatum">The sidecar-normalized final datum.</param>
/// <param name="Artifacts">The artifact manifest.</param>
/// <param name="Error">The optional run error.</param>
public sealed record BrowserRunStatusResponse(
    string RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? FinalDatum,
    IReadOnlyList<BrowserArtifactManifestItem> Artifacts,
    BrowserServiceError? Error);

/// <summary>
/// Describes one browser run artifact exposed by the sidecar manifest.
/// </summary>
/// <param name="Id">The opaque artifact identifier.</param>
/// <param name="Kind">The artifact kind.</param>
/// <param name="DisplayName">The human-readable artifact display name.</param>
/// <param name="ContentType">The artifact MIME content type.</param>
/// <param name="Bytes">The artifact byte length.</param>
public sealed record BrowserArtifactManifestItem(
    string Id,
    string Kind,
    string DisplayName,
    string ContentType,
    long Bytes);

/// <summary>
/// Browser artifact content downloaded from the sidecar.
/// </summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="ArtifactId">The artifact identifier.</param>
/// <param name="ContentType">The artifact MIME content type.</param>
/// <param name="Bytes">The downloaded artifact bytes.</param>
/// <param name="Truncated">Whether the bytes were truncated.</param>
public sealed record BrowserArtifactContent(
    string RunId,
    string ArtifactId,
    string ContentType,
    byte[] Bytes,
    bool Truncated);

/// <summary>
/// Response returned when cancelling a browser run.
/// </summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Status">The current run status after cancellation.</param>
/// <param name="Message">The cancellation message.</param>
public sealed record BrowserCancelRunResponse(
    string RunId,
    string Status,
    string Message);

/// <summary>
/// Sidecar error envelope.
/// </summary>
/// <param name="Code">The stable error code.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Details">Optional structured details.</param>
public sealed record BrowserServiceError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// Exception raised when the browser sidecar returns an error envelope.
/// </summary>
[SuppressMessage("Major Code Smell", "S3925", Justification = "Browser service exceptions are not serialized across process boundaries.")]
public sealed class BrowserServiceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserServiceException"/> class.
    /// </summary>
    /// <param name="code">The stable sidecar error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The optional HTTP status code.</param>
    /// <param name="details">Optional structured details.</param>
    public BrowserServiceException(
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

    /// <summary>
    /// Gets the stable sidecar error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the optional HTTP status code.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets optional structured details.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Details { get; }
}
