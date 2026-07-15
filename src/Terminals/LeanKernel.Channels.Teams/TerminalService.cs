using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Teams;

public sealed class TerminalService(
    ILogger<TerminalService> logger,
    ITransportClient transport,
    GatewayClient gatewayClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var activity = await transport.ReceiveAsync(stoppingToken);
            if (activity is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(activity.BearerToken))
            {
                logger.LogWarning("Rejecting Teams sender {SenderId}; no provisioned credential is available.", activity.SenderId);
                continue;
            }

            var attachments = AttachmentParser.Parse(activity.AttachmentUrls);
            var result = await gatewayClient.RunTurnAsync(activity.Text, activity.BearerToken, stoppingToken);
            if (attachments.Count > 0)
            {
                result = $"{result}\n\n(attachments={attachments.Count})";
            }

            await transport.SendAsync(activity, result, stoppingToken);
        }
    }
}

public sealed class GatewayClient(HttpClient httpClient, IOptions<GatewaySettings> settings)
{
    public async Task<string> RunTurnAsync(string input, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = settings.Value.Model,
            input,
            agent = new
            {
                name = settings.Value.AgentName
            }
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return $"Gateway request failed: {(int)response.StatusCode}";

        var payload = await response.Content.ReadAsStringAsync(ct);
        return ExtractResponseText(payload);
    }

    private static string ExtractResponseText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();

                foreach (var outputItem in output.EnumerateArray())
                {
                    if (!outputItem.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var contentItem in content.EnumerateArray())
                    {
                        if (!contentItem.TryGetProperty("type", out var typeElement)
                            || !string.Equals(typeElement.GetString(), "output_text", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var text = contentItem.TryGetProperty("text", out var textElement)
                            ? textElement.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        if (builder.Length > 0)
                            builder.AppendLine();

                        builder.Append(text);
                    }
                }

                if (builder.Length > 0)
                    return builder.ToString();
            }
        }
        catch (JsonException)
        {
            // Fallback to raw payload for non-JSON responses.
        }

        return payload;
    }
}

public interface ITransportClient
{
    Task<InboundActivity?> ReceiveAsync(CancellationToken ct);
    Task EnqueueAsync(InboundActivity activity, CancellationToken ct);
    Task SendAsync(InboundActivity inboundActivity, string text, CancellationToken ct);
}

public sealed class BotFrameworkTransportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<BotSettings> settings,
    IChannelCredentialProvider credentialProvider,
    ILogger<BotFrameworkTransportClient> logger) : ITransportClient
{
    private readonly Channel<InboundActivity> _channel = Channel.CreateUnbounded<InboundActivity>();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string _connectorToken = string.Empty;
    private DateTimeOffset _connectorTokenExpiresAt = DateTimeOffset.MinValue;

    public async Task<InboundActivity?> ReceiveAsync(CancellationToken ct)
    {
        if (await _channel.Reader.WaitToReadAsync(ct) && _channel.Reader.TryRead(out var activity))
        {
            var token = await credentialProvider.ResolveBearerTokenAsync(activity.SenderId, ct);
            return activity with { BearerToken = token };
        }

        return null;
    }

    public Task EnqueueAsync(InboundActivity activity, CancellationToken ct) =>
        _channel.Writer.WriteAsync(activity, ct).AsTask();

    public async Task SendAsync(InboundActivity inboundActivity, string text, CancellationToken ct)
    {
        if (!IsTrustedServiceUrl(inboundActivity.ServiceUrl, settings.Value.AllowedServiceUrlHostSuffixes))
        {
            logger.LogWarning("Skipping Teams reply because service URL is not trusted: {ServiceUrl}", inboundActivity.ServiceUrl);
            return;
        }

        var connectorToken = await GetConnectorTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(connectorToken))
        {
            logger.LogWarning("Skipping Teams reply because connector token could not be acquired.");
            return;
        }

        var connectorClient = httpClientFactory.CreateClient("teams-connector");
        connectorClient.BaseAddress = new Uri(inboundActivity.ServiceUrl);
        connectorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connectorToken);

        var activity = new
        {
            type = "message",
            text,
            replyToId = inboundActivity.ActivityId,
            conversation = new { id = inboundActivity.ConversationId },
            from = new { id = settings.Value.AppId },
            recipient = new { id = inboundActivity.SenderId }
        };

        using var response = await connectorClient.PostAsJsonAsync(
            $"/v3/conversations/{Uri.EscapeDataString(inboundActivity.ConversationId)}/activities",
            activity,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Teams send failed with status {StatusCode} for conversation {ConversationId}.", response.StatusCode, inboundActivity.ConversationId);
        }
    }

    private async Task<string> GetConnectorTokenAsync(CancellationToken ct)
    {
        if (_connectorTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(_connectorToken))
            return _connectorToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_connectorTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(_connectorToken))
                return _connectorToken;

            var authClient = httpClientFactory.CreateClient("teams-auth");
            using var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.Value.AppId,
                ["client_secret"] = settings.Value.AppPassword,
                ["scope"] = "https://api.botframework.com/.default"
            });

            using var response = await authClient.PostAsync("/botframework.com/oauth2/v2.0/token", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Teams connector token request failed with status {StatusCode}.", response.StatusCode);
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            _connectorToken = document.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 300;
            _connectorTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _connectorToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static bool IsTrustedServiceUrl(string value, IReadOnlyCollection<string> allowedHostSuffixes)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var suffix in allowedHostSuffixes)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                continue;

            if (uri.Host.EndsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public interface IChannelCredentialProvider
{
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}

public sealed class DatabaseChannelCredentialProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<DatabaseChannelCredentialProvider> logger) : IChannelCredentialProvider
{
    public async Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(senderId))
            return string.Empty;

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var matches = await context.ChannelSenderBindings
            .AsNoTracking()
            .Where(binding => binding.IsActive
                              && binding.Issuer == ChannelEntity.TeamsName
                              && binding.Subject == senderId
                              && binding.Channel.Name == "teams"
                              && !string.IsNullOrWhiteSpace(binding.BearerToken))
            .Select(binding => binding.BearerToken)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count > 1)
        {
            logger.LogWarning("Multiple active Teams bindings found for sender {SenderId}; refusing to select a token.", senderId);
            return string.Empty;
        }

        var token = matches.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No Teams JWT token found for sender {SenderId} in ChannelSenderBindings.", senderId);
        }

        return token;
    }
}

public sealed record InboundActivity(
    string ActivityId,
    string SenderId,
    string ConversationId,
    string ServiceUrl,
    string Text,
    string BearerToken,
    IReadOnlyList<string> AttachmentUrls);
