namespace LeanKernel.Services.Gateway.Providers;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;
using LeanKernel.Logic.Tools.BuiltIn;

using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that intercepts inbound requests with potential file attachments,
/// stages them to disk, and emits <see cref="DocumentIngestionRequestedEvent"/>
/// for asynchronous ingestion via the event subscriber pipeline.
/// Processes multipart/form-data uploads, channel JSON attachment envelopes,
/// and OpenAI-compatible content parts (chat completions <c>messages[].content</c>
/// and responses <c>input[].content</c>).
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

        if (scope == DocumentAvailabilityScope.Tenant && !permit.IsAuthenticated)
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
        var attachments = attachmentEnvelope?.Attachments ?? [];
        var channelIdHint = attachmentEnvelope?.ChannelId;
        var availabilityScope = attachmentEnvelope?.AvailabilityScope;
        var isOpenAiContentPartRequest = false;

        if (attachments.Count == 0)
        {
            attachments = await TryReadOpenAiContentPartsAsync(context, logger) ?? [];
            isOpenAiContentPartRequest = attachments.Count > 0;
            channelIdHint = null;
            availabilityScope = null;
        }

        if (attachments.Count == 0)
        {
            await next(context);
            return;
        }

        var scope = ResolveAvailabilityScope(availabilityScope);
        if (scope == DocumentAvailabilityScope.Tenant && !permit.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var (authorized, channelId) = await ResolveChannelAsync(
            channelIdHint,
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

        var staged = await StageFromJsonEnvelopeAsync(
            attachments,
            stagingDir,
            new IngestionContext(scope, permit.TenantId, permit.UserId, permit.PersonId, channelId),
            fileSettings.Value.MaxDownloadBytes,
            eventCollector,
            logger,
            context.RequestAborted);

        if (isOpenAiContentPartRequest && staged.Count > 0)
        {
            await InjectExtractedPartsAsync(context, staged, fileSettings.Value, logger, context.RequestAborted);
        }

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

    private static async Task<IReadOnlyList<JsonAttachmentPayload>?> TryReadOpenAiContentPartsAsync(
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
            if ((!TryReadProperty(root, "messages", out var itemsElement)
                && !TryReadProperty(root, "input", out itemsElement))
                || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var attachments = new List<JsonAttachmentPayload>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryReadProperty(item, "content", out var contentElement))
                {
                    continue;
                }

                if (contentElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in contentElement.EnumerateArray())
                    {
                        var payload = ReadOpenAiContentPart(part);
                        if (payload is not null)
                        {
                            attachments.Add(payload);
                        }
                    }
                }
                else if (contentElement.ValueKind == JsonValueKind.String
                    && IsDataUrl(contentElement.GetString()))
                {
                    attachments.Add(new JsonAttachmentPayload(string.Empty, string.Empty, contentElement.GetString()!));
                }
            }

            return attachments.Count > 0 ? attachments : null;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse OpenAI content parts from request body.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed reading OpenAI content parts for attachment ingestion middleware.");
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

    private static JsonAttachmentPayload? ReadOpenAiContentPart(JsonElement part)
    {
        if (part.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fileName = ReadStringProperty(part, "filename")
            ?? ReadStringProperty(part, "file_name")
            ?? ReadStringProperty(part, "name");

        // image_url part: { "type": "image_url", "image_url": { "url": "data:..." } } (chat completions)
        //              or: { "type": "input_image", "image_url": "data:..." }          (responses)
        if (TryReadProperty(part, "image_url", out var imageUrlElement))
        {
            var url = imageUrlElement.ValueKind == JsonValueKind.Object
                ? ReadStringProperty(imageUrlElement, "url")
                : imageUrlElement.ValueKind == JsonValueKind.String
                    ? imageUrlElement.GetString()
                    : null;

            if (IsDataUrl(url))
            {
                return new JsonAttachmentPayload(string.Empty, fileName ?? string.Empty, url!);
            }
        }

        // file part: { "type": "file", "file": { "file_data": "data:...", "filename": "..." } } (chat completions)
        // input_file parts carrying a file_id reference are skipped (no server-side file store to resolve them).
        if (TryReadProperty(part, "file", out var fileElement) && fileElement.ValueKind == JsonValueKind.Object)
        {
            var fileData = ReadStringProperty(fileElement, "file_data")
                ?? ReadStringProperty(fileElement, "fileData")
                ?? ReadStringProperty(fileElement, "data");
            var nestedFileName = ReadStringProperty(fileElement, "filename")
                ?? ReadStringProperty(fileElement, "file_name")
                ?? fileName;
            if (IsDataUrl(fileData))
            {
                return new JsonAttachmentPayload(string.Empty, nestedFileName ?? string.Empty, fileData!);
            }
        }

        var dataUrl = ReadStringProperty(part, "file_data")
            ?? ReadStringProperty(part, "fileData")
            ?? ReadStringProperty(part, "data")
            ?? ReadStringProperty(part, "url");
        if (IsDataUrl(dataUrl))
        {
            return new JsonAttachmentPayload(string.Empty, fileName ?? string.Empty, dataUrl!);
        }

        return null;
    }

    private static bool IsDataUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<StagedAttachment>> StageFromJsonEnvelopeAsync(
        IReadOnlyList<JsonAttachmentPayload> attachments,
        string stagingDir,
        IngestionContext ingestion,
        long maxDownloadBytes,
        IEventCollector eventCollector,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
        var staged = new List<StagedAttachment>();
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

            if (resolvedContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Skipping image attachment for document ingestion: {FileName}.", attachment.FileName);
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

            staged.Add(new StagedAttachment(safeName, stagedPath, attachment.FileDataUrl, resolvedContentType));
            logger.LogDebug("Staged JSON attachment for ingestion: {FileName}", safeName);
        }

        return staged;
    }

    private static async Task InjectExtractedPartsAsync(
        HttpContext context,
        IReadOnlyList<StagedAttachment> staged,
        FileSettings settings,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
        var replacements = await BuildInlineReplacementsAsync(staged, settings, logger, ct);
        if (replacements.Count == 0)
        {
            return;
        }

        var body = await TryReadJsonObjectAsync(context, ct);
        if (body is null)
        {
            return;
        }

        var containers = new List<(JsonArray Items, string PartType)>();
        if (TryGetCaseInsensitive(body, "messages", out var messagesNode)
            && messagesNode is JsonArray chatItems)
        {
            containers.Add((chatItems, "text"));
        }

        if (TryGetCaseInsensitive(body, "input", out var inputNode)
            && inputNode is JsonArray inputItems)
        {
            containers.Add((inputItems, "input_text"));
        }

        var rewritten = false;
        foreach (var (items, partType) in containers)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                if (!TryGetCaseInsensitive(item, "content", out var contentNode, out var contentKey))
                {
                    continue;
                }

                if (contentNode is JsonArray contentArray)
                {
                    for (var i = 0; i < contentArray.Count; i++)
                    {
                        if (contentArray[i] is JsonObject part
                            && TryGetPartDataUrl(part, out var dataUrl)
                            && replacements.TryGetValue(dataUrl, out var replacement))
                        {
                            contentArray[i] = BuildTextPart(partType, replacement);
                            rewritten = true;
                        }
                    }
                }
                else if (contentNode is JsonValue contentValue
                    && contentValue.TryGetValue<string>(out var contentString)
                    && replacements.TryGetValue(contentString, out var replacement))
                {
                    item[contentKey] = new JsonArray(BuildTextPart(partType, replacement));
                    rewritten = true;
                }
            }
        }

        if (!rewritten)
        {
            context.Request.Body.Position = 0;
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
    }

    private static async Task<Dictionary<string, string>> BuildInlineReplacementsAsync(
        IReadOnlyList<StagedAttachment> staged,
        FileSettings settings,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attachment in staged)
        {
            var text = await ExtractInlineTextAsync(attachment, settings, logger, ct);
            var replacement = string.IsNullOrWhiteSpace(text) || IsTruncated(text, settings.MaxExtractedCharacters)
                ? $"[Attached document \"{attachment.FileName}\" was uploaded to the document library. Ingestion is in progress — use the document_search tool to retrieve it after ingestion completes.]"
                : $"[Attached file: {attachment.FileName}]\n{text}";

            replacements[attachment.DataUrl] = replacement;
        }

        return replacements;
    }

    private static async Task<string> ExtractInlineTextAsync(
        StagedAttachment attachment,
        FileSettings settings,
        ILogger<AttachmentIngestionMiddleware> logger,
        CancellationToken ct)
    {
        if (!ShouldExtractInline(attachment))
        {
            return string.Empty;
        }

        try
        {
            return await TextExtractionHelper.ExtractAsync(
                attachment.StagedPath,
                settings.ScratchRoot,
                settings.PythonExecutable,
                settings.MaxExtractedCharacters,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            logger.LogWarning(ex, "Failed to extract inline text for attachment {FileName}.", attachment.FileName);
            return string.Empty;
        }
    }

    private static bool ShouldExtractInline(StagedAttachment attachment)
    {
        if (attachment.ResolvedContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (attachment.ResolvedContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || attachment.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(attachment.StagedPath);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return FileSystemSupport.IsTextLikeExtension(attachment.StagedPath)
            || FileSystemSupport.IsWordOpenXmlCandidate(attachment.StagedPath)
            || FileSystemSupport.IsSpreadsheetOpenXmlCandidate(attachment.StagedPath)
            || FileSystemSupport.IsPresentationOpenXmlCandidate(attachment.StagedPath)
            || FileSystemSupport.IsEpubCandidate(attachment.StagedPath)
            || FileSystemSupport.IsLegacyOfficeBinaryCandidate(attachment.StagedPath);
    }

    private static bool IsTruncated(string text, int maxExtractedCharacters)
        => text.Contains("[Content truncated to ", StringComparison.Ordinal)
            || text.Length > maxExtractedCharacters;

    private static JsonObject BuildTextPart(string type, string text)
        => new()
        {
            ["type"] = type,
            ["text"] = text,
        };

    private static bool TryGetPartDataUrl(JsonObject part, [NotNullWhen(true)] out string? dataUrl)
    {
        if (TryGetCaseInsensitive(part, "image_url", out var imageUrlNode))
        {
            if (imageUrlNode is JsonObject imageUrlObject
                && TryGetCaseInsensitive(imageUrlObject, "url", out var urlNode)
                && urlNode is JsonValue urlValue
                && urlValue.TryGetValue<string>(out var url)
                && IsDataUrl(url))
            {
                dataUrl = url;
                return true;
            }

            if (imageUrlNode is JsonValue imageUrlValue
                && imageUrlValue.TryGetValue<string>(out var urlString)
                && IsDataUrl(urlString))
            {
                dataUrl = urlString;
                return true;
            }
        }

        if (TryGetCaseInsensitive(part, "file", out var fileNode) && fileNode is JsonObject fileObject)
        {
            foreach (var propertyName in new[] { "file_data", "fileData", "data" })
            {
                if (TryGetCaseInsensitive(fileObject, propertyName, out var fileDataNode)
                    && fileDataNode is JsonValue fileDataValue
                    && fileDataValue.TryGetValue<string>(out var fileData)
                    && IsDataUrl(fileData))
                {
                    dataUrl = fileData;
                    return true;
                }
            }
        }

        foreach (var propertyName in new[] { "file_data", "fileData", "data", "url" })
        {
            if (TryGetCaseInsensitive(part, propertyName, out var flatNode)
                && flatNode is JsonValue flatValue
                && flatValue.TryGetValue<string>(out var flat)
                && IsDataUrl(flat))
            {
                dataUrl = flat;
                return true;
            }
        }

        dataUrl = null;
        return false;
    }

    private static bool TryGetCaseInsensitive(JsonObject obj, string propertyName, out JsonNode? value)
        => TryGetCaseInsensitive(obj, propertyName, out value, out _);

    private static bool TryGetCaseInsensitive(JsonObject obj, string propertyName, out JsonNode? value, out string matchedKey)
    {
        foreach (var (name, node) in obj)
        {
            if (string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = node;
                matchedKey = name;
                return true;
            }
        }

        value = null;
        matchedKey = propertyName;
        return false;
    }

    private static async Task<JsonObject?> TryReadJsonObjectAsync(HttpContext context, CancellationToken ct)
    {
        context.Request.Body.Position = 0;
        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
            return null;
        }

        if (node is JsonObject obj)
        {
            return obj;
        }

        context.Request.Body.Position = 0;
        return null;
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

        // Strip control characters (e.g. newlines and tabs) so untrusted file names cannot
        // break out of structured log entries or the prompt annotation rendered next to
        // extracted document text.
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) ? '_' : c);
        }

        return builder.ToString();
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

    private sealed record StagedAttachment(
        string FileName,
        string StagedPath,
        string DataUrl,
        string ResolvedContentType);

    private sealed record IngestionContext(
        DocumentAvailabilityScope Scope,
        Guid TenantId,
        Guid UserId,
        Guid PersonId,
        Guid ChannelId);
}