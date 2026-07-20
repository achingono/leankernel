using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;

using LeanKernel.Channels.Teams.Services;
using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Teams.Clients;

/// <summary>Transport client that communicates with the Bot Framework connector API.</summary>
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

    /// <summary>Receives the next inbound activity, resolving its bearer token.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The inbound activity with a resolved bearer token, or <c>null</c> if no activity is available.</returns>
    public async Task<InboundActivity?> ReceiveAsync(CancellationToken ct)
    {
        if (await _channel.Reader.WaitToReadAsync(ct) && _channel.Reader.TryRead(out var activity))
        {
            var token = await credentialProvider.ResolveBearerTokenAsync(activity.SenderId, ct);
            return activity with { BearerToken = token };
        }

        return null;
    }

    /// <summary>Enqueues an inbound activity for processing.</summary>
    /// <param name="activity">The activity to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task EnqueueAsync(InboundActivity activity, CancellationToken ct) =>
        _channel.Writer.WriteAsync(activity, ct).AsTask();

    /// <summary>Sends a reply message to the Teams conversation.</summary>
    /// <param name="inboundActivity">The original inbound activity to reply to.</param>
    /// <param name="text">The reply text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
        {
            return _connectorToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_connectorTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(_connectorToken))
            {
                return _connectorToken;
            }

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
            {
                continue;
            }

            if (uri.Host.EndsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
