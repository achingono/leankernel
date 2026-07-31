namespace LeanKernel.Services.Gateway.Requests;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>
/// Maps the document upload Minimal API endpoints.
/// </summary>
public static class DocumentUploadEndpoint
{
    /// <summary>
    /// Maps the POST /api/documents/upload endpoint that accepts a file and identity metadata,
    /// stages it on disk, and enqueues it for ingestion.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void MapDocumentUpload(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents/upload", HandleUploadAsync)
        .RequireAuthorization()
        .DisableAntiforgery();

        endpoints.MapPost("/api/documents/ingest", HandleIngestBase64Async)
        .RequireAuthorization()
        .DisableAntiforgery();
    }

    private static async Task<IResult> HandleIngestBase64Async(
        HttpContext context,
        [FromBody] IngestDocumentRequest request,
        [FromServices] IPermit permit,
        [FromServices] IDocumentIngestionQueue queue,
        [FromServices] IOptions<FileSettings> fileSettings)
    {
        if (string.IsNullOrWhiteSpace(request?.FileData))
        {
            return Results.BadRequest(new { error = "file_data is required." });
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new { error = "file_name is required." });
        }

        var channelResolution = await ResolveChannelAsync(context, permit, request.ChannelId);
        if (!channelResolution.Allowed)
        {
            return Results.Forbid();
        }

        var channelId = channelResolution.ChannelId;

        var scope = ParseAvailabilityScope(request.AvailabilityScope);

        if (scope == DocumentAvailabilityScope.Tenant && permit.Badge.Id == Guid.Empty)
        {
            return Results.Forbid();
        }

        var decodedFile = DecodeFileData(request.FileData, request.ContentType);
        if (!decodedFile.Success)
        {
            return Results.BadRequest(new { error = decodedFile.ErrorMessage });
        }

        var maxBytes = fileSettings.Value.MaxDownloadBytes;
        if (maxBytes > 0 && decodedFile.Bytes.Length > maxBytes)
        {
            return Results.BadRequest(new { error = $"file_data exceeds maximum size of {maxBytes} bytes." });
        }

        var tenantId = permit.TenantId;
        var userId = permit.UserId;
        var personId = permit.PersonId;

        var stagingDir = Path.Combine(
            fileSettings.Value.RootPath,
            "documents",
            tenantId.ToString(),
            scope.ToString().ToLowerInvariant(),
            channelId.ToString(),
            userId.ToString(),
            "_staging");

        Directory.CreateDirectory(stagingDir);
        var safeName = SanitizeFileName(request.FileName);
        var stagedPath = Path.Combine(stagingDir, safeName);

        if (!stagedPath.StartsWith(stagingDir, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Invalid file name." });
        }

        await File.WriteAllBytesAsync(stagedPath, decodedFile.Bytes, context.RequestAborted);

        var job = new DocumentIngestionJob(
            stagedPath,
            safeName,
            decodedFile.ContentType,
            tenantId,
            userId,
            personId,
            channelId,
            scope,
            DocumentIngestionSource.Upload);

        await queue.EnqueueAsync(job, context.RequestAborted);

        return Results.Accepted($"/api/documents/jobs/{job.GetHashCode()}", new { status = "queued" });
    }

    private static async Task<ChannelResolution> ResolveChannelAsync(HttpContext context, IPermit permit, string? channelIdValue)
    {
        if (string.IsNullOrWhiteSpace(channelIdValue)
            || !Guid.TryParse(channelIdValue, out var parsedChannelId))
        {
            return new ChannelResolution(true, permit.ChannelId);
        }

        var policyResolver = context.RequestServices.GetRequiredService<IChannelMemoryPolicyResolver>();
        var policy = await policyResolver.ResolveAsync(permit.TenantId, permit.ChannelId, context.RequestAborted);
        var readableChannels = policy.ReadableChannelIds.Append(permit.ChannelId).Distinct().ToList();
        return new ChannelResolution(readableChannels.Contains(parsedChannelId), parsedChannelId);
    }

    private static DecodedFileData DecodeFileData(string fileData, string? requestedContentType)
    {
        var base64Data = fileData;
        var contentType = string.IsNullOrWhiteSpace(requestedContentType)
            ? Constants.ContentTypes.ApplicationOctetStream
            : requestedContentType;

        if (base64Data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var semicolonIndex = base64Data.IndexOf(';', StringComparison.Ordinal);
            if (semicolonIndex > 5)
            {
                var declaredType = base64Data[5..semicolonIndex];
                if (!string.IsNullOrWhiteSpace(declaredType))
                {
                    contentType = declaredType;
                }
            }

            var commaIndex = base64Data.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex >= 0)
            {
                base64Data = base64Data[(commaIndex + 1)..];
            }
        }

        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            if (bytes.Length == 0)
            {
                return new DecodedFileData(false, [], contentType, "file_data is empty.");
            }

            return new DecodedFileData(true, bytes, contentType, string.Empty);
        }
        catch (FormatException)
        {
            return new DecodedFileData(false, [], contentType, "file_data is not valid base64.");
        }
    }

    private static async Task<IResult> HandleUploadAsync(
        HttpContext context,
        [FromForm] IFormFile file,
        [FromForm] string channel_id,
        [FromForm] string? availability_scope,
        [FromServices] IPermit permit,
        [FromServices] IDocumentIngestionQueue queue)
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "File is required." });
        }

        if (string.IsNullOrWhiteSpace(channel_id))
        {
            return Results.BadRequest(new { error = "channel_id is required." });
        }

        if (!Guid.TryParse(channel_id, out var channelId))
        {
            return Results.BadRequest(new { error = "channel_id must be a valid GUID." });
        }

        var policyResolver = context.RequestServices.GetRequiredService<IChannelMemoryPolicyResolver>();
        var fileSettings = context.RequestServices.GetRequiredService<IOptions<FileSettings>>();

        var policy = await policyResolver.ResolveAsync(permit.TenantId, permit.ChannelId, context.RequestAborted);
        var readableChannels = policy.ReadableChannelIds.Append(permit.ChannelId).Distinct().ToList();
        if (!readableChannels.Contains(channelId))
        {
            return Results.Forbid();
        }

        var scope = ParseAvailabilityScope(availability_scope);

        if (scope == DocumentAvailabilityScope.Tenant && permit.Badge.Id == Guid.Empty)
        {
            return Results.Forbid();
        }

        var tenantId = permit.TenantId;
        var userId = permit.UserId;
        var personId = permit.PersonId;

        var stagingDir = Path.Combine(
            fileSettings.Value.RootPath,
            "documents",
            tenantId.ToString(),
            scope.ToString().ToLowerInvariant(),
            channelId.ToString(),
            userId.ToString(),
            "_staging");

        Directory.CreateDirectory(stagingDir);
        var safeName = SanitizeFileName(file.FileName);
        var stagedPath = Path.Combine(stagingDir, safeName);

        if (!stagedPath.StartsWith(stagingDir, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Invalid file name." });
        }

        await using (var stream = new FileStream(stagedPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var job = new DocumentIngestionJob(
            stagedPath,
            safeName,
            file.ContentType,
            tenantId,
            userId,
            personId,
            channelId,
            scope,
            DocumentIngestionSource.Upload);

        await queue.EnqueueAsync(job, context.RequestAborted);

        return Results.Accepted(
            $"/api/documents/jobs/{job.GetHashCode()}",
            new { status = "queued" });
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(name))
        {
            return Guid.NewGuid().ToString("N");
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private static DocumentAvailabilityScope ParseAvailabilityScope(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DocumentAvailabilityScope.User;
        }

        return value.ToLowerInvariant() switch
        {
            "tenant" => DocumentAvailabilityScope.Tenant,
            "user" => DocumentAvailabilityScope.User,
            "channel" => DocumentAvailabilityScope.Channel,
            _ => DocumentAvailabilityScope.User,
        };
    }

    private readonly record struct ChannelResolution(bool Allowed, Guid ChannelId);

    private readonly record struct DecodedFileData(
        bool Success,
        byte[] Bytes,
        string ContentType,
        string ErrorMessage);
}