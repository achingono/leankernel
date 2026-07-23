namespace LeanKernel.Gateway.Providers;

using System.Diagnostics.CodeAnalysis;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that intercepts inbound requests with potential file attachments,
/// stages them to disk, and emits <see cref="DocumentIngestionRequestedEvent"/>
/// for asynchronous ingestion via the event subscriber pipeline.
/// Only processes multipart/form-data requests; JSON requests pass through unchanged.
/// Must run after <see cref="TenantResolutionMiddleware"/> so that identity is available.
/// </summary>
public sealed class AttachmentIngestionMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Invokes the middleware, staging any file attachments found in multipart requests.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="permit">The request identity permit.</param>
    /// <param name="fileSettings">The file settings for staging path resolution.</param>
    /// <param name="eventCollector">The scoped event collector for emitting ingestion events.</param>
    /// <param name="policyResolver">The channel memory policy resolver for channel authorization.</param>
    /// <param name="logger">The logger instance.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Major Code Smell", "S4457", Justification = "Middleware reads multipart form which requires async I/O before invoking next.")]
    public async Task InvokeAsync(
        HttpContext context,
        IPermit permit,
        IOptions<FileSettings> fileSettings,
        IEventCollector eventCollector,
        IChannelMemoryPolicyResolver policyResolver,
        ILogger<AttachmentIngestionMiddleware> logger)
    {
        if (!IsMultipartRequest(context.Request))
        {
            await next(context);
            return;
        }

        List<IFormFile>? files;
        try
        {
            context.Request.EnableBuffering();

            if (!context.Request.HasFormContentType)
            {
                context.Request.Body.Position = 0;
                await next(context);
                return;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            files = form.Files.Where(f => f.Length > 0).ToList();

            context.Request.Body.Position = 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read multipart form for attachment ingestion");
            context.Request.Body.Position = 0;
            await next(context);
            return;
        }

        if (files.Count == 0)
        {
            await next(context);
            return;
        }

        var scope = DocumentAvailabilityScope.User;
        if (context.Request.Form.TryGetValue("availability_scope", out var scopeVal))
        {
            _ = Enum.TryParse(scopeVal, ignoreCase: true, out scope);
        }

        if (scope == DocumentAvailabilityScope.Tenant && permit.Badge.Id == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tenantId = permit.TenantId;
        var userId = permit.UserId;
        var personId = permit.PersonId;

        Guid channelId;
        if (context.Request.Form.TryGetValue("channel_id", out var channelIdVal)
            && Guid.TryParse(channelIdVal, out var parsedChannelId))
        {
            var policy = await policyResolver.ResolveAsync(tenantId, permit.ChannelId, context.RequestAborted);
            var readableChannels = policy.ReadableChannelIds.Append(permit.ChannelId).Distinct().ToList();
            if (!readableChannels.Contains(parsedChannelId))
            {
                context.Response.StatusCode = 403;
                return;
            }

            channelId = parsedChannelId;
        }
        else
        {
            channelId = permit.ChannelId;
        }

        var stagingDir = Path.Combine(
            fileSettings.Value.RootPath,
            "documents",
            tenantId.ToString(),
            scope.ToString().ToLowerInvariant(),
            channelId.ToString(),
            userId.ToString(),
            "_staging");

        Directory.CreateDirectory(stagingDir);

        foreach (var file in files)
        {
            try
            {
                var safeName = SanitizeFileName(file.FileName);
                var stagedPath = Path.Combine(stagingDir, safeName);

                if (!stagedPath.StartsWith(stagingDir, StringComparison.Ordinal))
                {
                    logger.LogWarning("Rejected path traversal attempt in filename: {FileName}", file.FileName);
                    continue;
                }

                await using (var stream = new FileStream(stagedPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var envelope = new EventEnvelope
                {
                    EventType = "document_ingestion",
                    TenantId = tenantId,
                    PersonId = personId,
                    UserId = userId,
                    ChannelId = channelId,
                };

                eventCollector.Emit(new DocumentIngestionRequestedEvent
                {
                    Envelope = envelope,
                    StagedFilePath = stagedPath,
                    FileName = safeName,
                    ContentType = file.ContentType,
                    AvailabilityScope = scope,
                    TenantId = tenantId,
                    UserId = userId,
                    PersonId = personId,
                    ChannelId = channelId,
                });

                logger.LogDebug("Staged attachment for ingestion: {FileName}", safeName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stage attachment: {FileName}", file.FileName);
            }
        }

        await next(context);
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

    private static bool IsMultipartRequest(HttpRequest request)
    {
        var contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
    }
}
