namespace LeanKernel.Services.Gateway.Providers;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

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
/// Processes multipart/form-data uploads and channel JSON attachment envelopes.
/// Must run after <see cref="TenantResolutionMiddleware"/> so that identity is available.
/// </summary>
public sealed class AttachmentIngestionMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Invokes the middleware, staging multipart file uploads and JSON envelope attachments.
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
        if (IsMultipartRequest(context.Request))
        {
            await HandleMultipartAsync(context, permit, fileSettings, eventCollector, policyResolver, logger);
            return;
        }

        if (IsJsonRequest(context.Request))
        {
            await HandleJsonEnvelopeAsync(context, permit, fileSettings, eventCollector, policyResolver, logger);
            return;
        }

        await next(context);
    }

    private async Task HandleMultipartAsync(
        HttpContext context,
        IPermit permit,
        IOptions<FileSettings> fileSettings,
        IEventCollector eventCollector,
        IChannelMemoryPolicyResolver policyResolver,
        ILogger<AttachmentIngestionMiddleware> logger)
    {
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

    private async Task HandleJsonEnvelopeAsync(
        HttpContext context,
        IPermit permit,
        IOptions<FileSettings> fileSettings,
        IEventCollector eventCollector,
        IChannelMemoryPolicyResolver policyResolver,
        ILogger<AttachmentIngestionMiddleware> logger)
    {
        var attachmentEnvelope = await TryReadJsonEnvelopeAsync(context, logger);
        if (attachmentEnvelope is null || attachmentEnvelope.Attachments.Count == 0)
        {
            await next(context);
            return;
        }

        var scope = ResolveAvailabilityScope(attachmentEnvelope.AvailabilityScope);
        if (scope == DocumentAvailabilityScope.Tenant && permit.Badge.Id == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var (authorized, channelId) = await ResolveChannelAsync(
            attachmentEnvelope.ChannelId,
            permit,
            policyResolver,
            context.RequestAborted);
        if (!authorized)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var stagingDir = Path.Combine(
            fileSettings.Value.RootPath,
            "documents",
            permit.TenantId.ToString(),
            scope.ToString().ToLowerInvariant(),
            channelId.ToString(),
            permit.UserId.ToString(),
            "_staging");

        Directory.CreateDirectory(stagingDir);

        await StageFromJsonEnvelopeAsync(
            attachmentEnvelope.Attachments,
            stagingDir,
            new IngestionContext(scope, permit.TenantId, permit.UserId, permit.PersonId, channelId),
            fileSettings.Value.MaxDownloadBytes,
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
        if (form.TryGetValue("availability_scope", out var scopeVal))
        {
            return ResolveAvailabilityScope(scopeVal.ToString());
        }

        return ResolveAvailabilityScope((string?)null);
    }

    private static DocumentAvailabilityScope ResolveAvailabilityScope(string? availabilityScope)
    {
        if (!string.IsNullOrWhiteSpace(availabilityScope)
            && Enum.TryParse(availabilityScope, ignoreCase: true, out DocumentAvailabilityScope parsed))
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
        if (!form.TryGetValue("channel_id", out var channelIdVal))
        {
            return (true, permit.ChannelId);
        }

        return await ResolveChannelAsync(channelIdVal.ToString(), permit, policyResolver, ct);
    }

    private static async Task<(bool Authorized, Guid ChannelId)> ResolveChannelAsync(
        string? channelId,
        IPermit permit,
        IChannelMemoryPolicyResolver policyResolver,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId)
            || !Guid.TryParse(channelId, out var parsedChannelId))
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

                eventCollector.Emit(new DocumentIngestionRequestedEvent
                {
                    Envelope = BuildDocumentEnvelope(ingestion),
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

    private static async Task<JsonAttachmentEnvelope?> TryReadJsonEnvelopeAsync(
        HttpContext context,
        ILogger<AttachmentIngestionMiddleware> logger)
    {
        try
        {
            context.Request.EnableBuffering();

            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            if ((!TryReadProperty(root, "channel_attachments", out var attachmentsElement)
                && !TryReadProperty(root, "channelAttachments", out attachmentsElement))
                || attachmentsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var attachments = new List<JsonAttachmentPayload>();
            foreach (var attachmentElement in attachmentsElement.EnumerateArray())
            {
                if (attachmentElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var contentType = ReadStringProperty(attachmentElement, "contentType")
                    ?? ReadStringProperty(attachmentElement, "content_type")
                    ?? string.Empty;
                var fileName = ReadStringProperty(attachmentElement, "fileName")
                    ?? ReadStringProperty(attachmentElement, "file_name")
                    ?? string.Empty;
                var fileDataUrl = ReadStringProperty(attachmentElement, "fileDataUrl")
                    ?? ReadStringProperty(attachmentElement, "file_data_url")
                    ?? string.Empty;

                attachments.Add(new JsonAttachmentPayload(contentType, fileName, fileDataUrl));
            }

            var channelId = ReadStringProperty(root, "channelId")
                ?? ReadStringProperty(root, "channel_id");
            var availabilityScope = ReadStringProperty(root, "availabilityScope")
                ?? ReadStringProperty(root, "availability_scope");

            return new JsonAttachmentEnvelope(attachments, channelId, availabilityScope);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse JSON attachment envelope from request body.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed reading JSON request body for attachment ingestion middleware.");
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

    private static async Task StageFromJsonEnvelopeAsync(
        IReadOnlyList<JsonAttachmentPayload> attachments,
        string stagingDir,
        IngestionContext ingestion,
        long maxDownloadBytes,
        IEventCollector eventCollector,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileDataUrl)
                || attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryDecodeDataUrl(attachment.FileDataUrl, attachment.ContentType, out var bytes, out var resolvedContentType))
            {
                logger.LogDebug("Skipping malformed attachment data URL for file {FileName}.", attachment.FileName);
                continue;
            }

            if (bytes.Length == 0)
            {
                continue;
            }

            if (maxDownloadBytes > 0 && bytes.Length > maxDownloadBytes)
            {
                logger.LogInformation(
                    "Skipping JSON attachment {FileName}: size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.FileName,
                    bytes.Length,
                    maxDownloadBytes);
                continue;
            }

            var fileName = string.IsNullOrWhiteSpace(attachment.FileName)
                ? Guid.NewGuid().ToString("N")
                : attachment.FileName;
            var safeName = SanitizeFileName(fileName);
            var stagedPath = Path.Combine(stagingDir, safeName);

            if (!stagedPath.StartsWith(stagingDir, StringComparison.Ordinal))
            {
                logger.LogWarning("Rejected path traversal attempt in JSON filename: {FileName}", fileName);
                continue;
            }

            await File.WriteAllBytesAsync(stagedPath, bytes, ct);

            eventCollector.Emit(new DocumentIngestionRequestedEvent
            {
                Envelope = BuildDocumentEnvelope(ingestion),
                StagedFilePath = stagedPath,
                FileName = safeName,
                ContentType = resolvedContentType,
                AvailabilityScope = ingestion.Scope,
                TenantId = ingestion.TenantId,
                UserId = ingestion.UserId,
                PersonId = ingestion.PersonId,
                ChannelId = ingestion.ChannelId,
            });

            logger.LogDebug("Staged JSON attachment for ingestion: {FileName}", safeName);
        }
    }

    private static bool TryDecodeDataUrl(
        string fileDataUrl,
        string fallbackContentType,
        out byte[] bytes,
        out string resolvedContentType)
    {
        bytes = [];
        resolvedContentType = string.IsNullOrWhiteSpace(fallbackContentType)
            ? Constants.ContentTypes.ApplicationOctetStream
            : fallbackContentType;

        var payload = fileDataUrl;
        if (fileDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var semicolonIndex = fileDataUrl.IndexOf(';', StringComparison.Ordinal);
            if (semicolonIndex > 5)
            {
                resolvedContentType = fileDataUrl[5..semicolonIndex];
            }

            var commaIndex = fileDataUrl.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex < 0 || commaIndex >= fileDataUrl.Length - 1)
            {
                return false;
            }

            payload = fileDataUrl[(commaIndex + 1)..];
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static EventEnvelope BuildDocumentEnvelope(IngestionContext ingestion)
        => new()
        {
            EventType = "document_ingestion",
            TenantId = ingestion.TenantId,
            PersonId = ingestion.PersonId,
            UserId = ingestion.UserId,
            ChannelId = ingestion.ChannelId,
        };

    private static bool TryReadProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!TryReadProperty(element, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
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

    private static bool IsJsonRequest(HttpRequest request)
    {
        var contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith(Constants.ContentTypes.Json, StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record JsonAttachmentEnvelope(
        IReadOnlyList<JsonAttachmentPayload> Attachments,
        string? ChannelId,
        string? AvailabilityScope);

    private sealed record JsonAttachmentPayload(
        string ContentType,
        string FileName,
        string FileDataUrl);

    private sealed record IngestionContext(
        DocumentAvailabilityScope Scope,
        Guid TenantId,
        Guid UserId,
        Guid PersonId,
        Guid ChannelId);
}