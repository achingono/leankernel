namespace LeanKernel.Gateway.Requests;

using System.Text.Json;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>
/// Maps the document upload Minimal API endpoint.
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
}
