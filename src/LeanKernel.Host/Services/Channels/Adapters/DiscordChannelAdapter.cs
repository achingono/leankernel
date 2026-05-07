using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services.Channels.Adapters;

/// <summary>
/// Discord channel adapter for sending messages via Discord REST API.
/// Requires Discord bot token and channel ID in LeanKernelConfig.
/// </summary>
public sealed class DiscordChannelAdapter : IMessageChannel
{
    private readonly ILogger<DiscordChannelAdapter> _logger;
    private readonly string? _botToken;
    private readonly string? _channelId;
    private readonly HttpClient _httpClient;
    private const string DiscordApiBase = "https://discord.com/api/v10";
    private const int MaxRetries = 3;
    private const int BaseRetryDelaySeconds = 2;
    private const int RateLimitRetryDelaySeconds = 60;

    public string Name => "Discord";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_botToken) &&
        !string.IsNullOrWhiteSpace(_channelId);

    public DiscordChannelAdapter(
        ILogger<DiscordChannelAdapter> logger,
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

    public async Task<ChannelDeliveryResult> DeliverAsync(
        string recipient,
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
            var deliveryResult = await SendMessageWithRetryAsync(content, ct);
            return deliveryResult;
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

    private async Task<ChannelDeliveryResult> SendMessageWithRetryAsync(
        string content,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
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
            content = content,
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
            var idIndex = responseContent.IndexOf("\"id\"");
            if (idIndex >= 0)
            {
                var colonIndex = responseContent.IndexOf(":", idIndex);
                var quoteStart = responseContent.IndexOf("\"", colonIndex) + 1;
                var quoteEnd = responseContent.IndexOf("\"", quoteStart);
                return responseContent.Substring(quoteStart, quoteEnd - quoteStart);
            }
        }
        catch
        {
            // Fall through to default ID
        }

        return Guid.NewGuid().ToString();
    }

    private sealed record DiscordSendResult(string? MessageId, int? RetryAfterSeconds)
    {
        public static DiscordSendResult Successful(string messageId) => new(messageId, null);

        public static DiscordSendResult RateLimited(int retryAfterSeconds) => new(null, retryAfterSeconds);
    }
}
