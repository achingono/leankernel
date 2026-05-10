using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Discord channel adapter for sending messages through the Discord REST API.
/// </summary>
public sealed class DiscordChannelAdapter : IChannel
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _botToken;
    private readonly string? _channelId;
    private const string DiscordApiBase = "https://discord.com/api/v10";
    private const int MaxRetries = 3;
    private const int BaseRetryDelaySeconds = 2;
    private const int RateLimitRetryDelaySeconds = 60;

    /// <inheritdoc />
    public string ChannelId => "discord";

    /// <inheritdoc />
    public string Name => "Discord";

    /// <inheritdoc />
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_botToken) &&
        !string.IsNullOrWhiteSpace(_channelId);

    /// <inheritdoc />
    public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordChannelAdapter" /> class.
    /// </summary>
    /// <param name="logger">The logger used for delivery diagnostics.</param>
    /// <param name="httpClient">The HTTP client used for Discord API requests.</param>
    /// <param name="botToken">The Discord bot token.</param>
    /// <param name="channelId">The Discord channel identifier.</param>
    public DiscordChannelAdapter(
        ILogger logger,
        HttpClient httpClient,
        string? botToken,
        string? channelId)
    {
        _logger = logger;
        _httpClient = httpClient;
        _botToken = botToken;
        _channelId = channelId;

        if (!IsConfigured)
        {
            _logger.LogWarning("Discord channel is not properly configured");
        }
    }

    /// <inheritdoc />
    public bool IsAuthorizedSender(string senderId) => true;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task SendAsync(string recipientId, string content, CancellationToken ct)
    {
        var result = await DeliverAsync(recipientId, content, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Discord delivery failed.");
        }
    }

    /// <inheritdoc />
    public async Task<ChannelDeliveryResult> DeliverAsync(
        string recipientId,
        string content,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Discord channel is not configured",
                retryable: false);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Message content cannot be empty",
                retryable: false);
        }

        try
        {
            return await SendMessageWithRetryAsync(content, ct);
        }
        catch (OperationCanceledException)
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Message delivery was cancelled",
                retryable: true,
                TimeSpan.FromSeconds(BaseRetryDelaySeconds * 2));
        }
        catch (HttpRequestException ex)
        {
            return ChannelDeliveryResult.Failed(
                Name,
                $"Network error: {ex.Message}",
                retryable: true,
                TimeSpan.FromSeconds(BaseRetryDelaySeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending Discord message");
            return ChannelDeliveryResult.Failed(
                Name,
                $"Unexpected error: {ex.Message}",
                retryable: false);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<ChannelDeliveryResult> SendMessageWithRetryAsync(
        string content,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var result = await SendDirectAsync(content, ct);
                if (result.RetryAfterSeconds is { } retryAfterSeconds)
                {
                    if (attempt < MaxRetries - 1)
                    {
                        _logger.LogWarning(
                            "Discord rate limited, retrying in {Delay}s",
                            retryAfterSeconds);

                        await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds), ct);
                        continue;
                    }

                    _logger.LogError(
                        "Discord rate limited and exhausted retries. Retry after: {Delay}s",
                        retryAfterSeconds);

                    return ChannelDeliveryResult.Failed(
                        Name,
                        $"Rate limited: Rate limited, retry after {retryAfterSeconds}s",
                        retryable: true,
                        TimeSpan.FromSeconds(retryAfterSeconds));
                }

                _logger.LogInformation(
                    "Successfully sent Discord message (ID: {MessageId}, attempt: {Attempt})",
                    result.MessageId,
                    attempt + 1);

                return ChannelDeliveryResult.Successful(Name, result.MessageId!);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries - 1)
            {
                var delay = BaseRetryDelaySeconds * (attempt + 1);
                _logger.LogWarning(
                    "Discord delivery attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt + 1,
                    ex.Message,
                    delay);

                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    "Discord delivery failed after {MaxRetries} attempts: {Error}",
                    MaxRetries,
                    ex.Message);
                throw;
            }
        }

        throw new InvalidOperationException("Message delivery retry logic failed");
    }

    private async Task<DiscordSendResult> SendDirectAsync(string content, CancellationToken ct)
    {
        var url = $"{DiscordApiBase}/channels/{_channelId}/messages";
        var payload = new
        {
            content,
            tts = false
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        request.Headers.Add("Authorization", $"Bot {_botToken}");
        request.Headers.Add("User-Agent", "LeanKernel/1.0");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = ExtractRetryAfter(response.Headers);
            return DiscordSendResult.RateLimited(retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Discord API returned {(int)response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return DiscordSendResult.Successful(ExtractMessageId(responseContent));
    }

    private static int ExtractRetryAfter(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfterStr = values.FirstOrDefault();
            if (int.TryParse(retryAfterStr, out var seconds))
            {
                return Math.Max(seconds, 1);
            }
        }

        return RateLimitRetryDelaySeconds;
    }

    private static string ExtractMessageId(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
            return Guid.NewGuid().ToString();

        try
        {
            var idIndex = responseContent.IndexOf("\"id\"", StringComparison.Ordinal);
            if (idIndex >= 0)
            {
                var colonIndex = responseContent.IndexOf(":", idIndex, StringComparison.Ordinal);
                var quoteStart = responseContent.IndexOf("\"", colonIndex, StringComparison.Ordinal) + 1;
                var quoteEnd = responseContent.IndexOf("\"", quoteStart, StringComparison.Ordinal);
                return responseContent.Substring(quoteStart, quoteEnd - quoteStart);
            }
        }
        catch
        {
            // Fall through to generated fallback ID.
        }

        return Guid.NewGuid().ToString();
    }

    private sealed record DiscordSendResult(string? MessageId, int? RetryAfterSeconds)
    {
        public static DiscordSendResult Successful(string messageId) => new(messageId, null);

        public static DiscordSendResult RateLimited(int retryAfterSeconds) => new(null, retryAfterSeconds);
    }
}
