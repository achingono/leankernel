namespace LeanKernel.Services.Gateway.Providers;

using System.Diagnostics.CodeAnalysis;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;

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

        var multipart = await TryReadMultipartAsync(context, logger);
        if (multipart is null || multipart.Value.Files.Count == 0)
        {
            await next(context);
            return;
        }

        var form = multipart.Value.Form;
        var files = multipart.Value.Files;
        var scope = ResolveAvailabilityScope(form);

        if (scope == DocumentAvailabilityScope.Tenant && permit.Badge.Id == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tenantId = permit.TenantId;
        var userId = permit.UserId;
        var personId = permit.PersonId;

        var (authorized, channelId) = await ResolveChannelAsync(form, permit, policyResolver, context.RequestAborted);
        if (!authorized)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
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

        await StageAndEmitAsync(
            files,
            stagingDir,
            new IngestionContext(scope, tenantId, userId, personId, channelId),
            eventCollector,
            logger,
            context.RequestAborted);

        await next(context);
    }

    private static async Task<(IFormCollection Form, List<IFormFile> Files)?> TryReadMultipartAsync(
        HttpContext context,
        ILogger<AttachmentIngestionMiddleware> logger)
    {
        try
        {
            context.Request.EnableBuffering();

            if (!context.Request.HasFormContentType)
            {
                return null;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var files = form.Files.Where(f => f.Length > 0).ToList();
            return (form, files);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read multipart form for attachment ingestion");
            return null;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }
        }
    }

    private static DocumentAvailabilityScope ResolveAvailabilityScope(IFormCollection form)
    {
        if (form.TryGetValue("availability_scope", out var scopeVal)
            && Enum.TryParse(scopeVal, ignoreCase: true, out DocumentAvailabilityScope parsed))
        {
            return parsed;
        }

        return DocumentAvailabilityScope.User;
    }

    private static async Task<(bool Authorized, Guid ChannelId)> ResolveChannelAsync(
        IFormCollection form,
        IPermit permit,
        IChannelMemoryPolicyResolver policyResolver,
        CancellationToken ct)
    {
        if (!form.TryGetValue("channel_id", out var channelIdVal)
            || !Guid.TryParse(channelIdVal, out var parsedChannelId))
        {
            return (true, permit.ChannelId);
        }

        var policy = await policyResolver.ResolveAsync(permit.TenantId, permit.ChannelId, ct);
        var readableChannels = policy.ReadableChannelIds.Append(permit.ChannelId).Distinct().ToList();
        return readableChannels.Contains(parsedChannelId)
            ? (true, parsedChannelId)
            : (false, Guid.Empty);
    }

    private static async Task StageAndEmitAsync(
        IReadOnlyList<IFormFile> files,
        string stagingDir,
        IngestionContext ingestion,
        IEventCollector eventCollector,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
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
                    await file.CopyToAsync(stream, ct);
                }

                var envelope = new EventEnvelope
                {
                    EventType = "document_ingestion",
                    TenantId = ingestion.TenantId,
                    PersonId = ingestion.PersonId,
                    UserId = ingestion.UserId,
                    ChannelId = ingestion.ChannelId,
                };

                eventCollector.Emit(new DocumentIngestionRequestedEvent
                {
                    Envelope = envelope,
                    StagedFilePath = stagedPath,
                    FileName = safeName,
                    ContentType = file.ContentType,
                    AvailabilityScope = ingestion.Scope,
                    TenantId = ingestion.TenantId,
                    UserId = ingestion.UserId,
                    PersonId = ingestion.PersonId,
                    ChannelId = ingestion.ChannelId,
                });

                logger.LogDebug("Staged attachment for ingestion: {FileName}", safeName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stage attachment: {FileName}", file.FileName);
            }
        }
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

    private sealed record IngestionContext(
        DocumentAvailabilityScope Scope,
        Guid TenantId,
        Guid UserId,
        Guid PersonId,
        Guid ChannelId);
}
