using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services.Channels.Adapters;

/// <summary>
/// Signal Messenger channel adapter for sending messages via Signal API.
/// Requires Signal to be configured with API credentials in LeanKernelConfig.
/// </summary>
public sealed class SignalChannelAdapter : IMessageChannel
{
    private readonly ILogger<SignalChannelAdapter> _logger;
    private readonly string? _phoneNumber;
    private readonly string? _serverUrl;
    private readonly string? _apiToken;
    private readonly HttpClient _httpClient;
    private const int MaxRetries = 3;
    private const int BaseRetryDelaySeconds = 2;

    public string Name => "Signal";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_phoneNumber) &&
        !string.IsNullOrWhiteSpace(_serverUrl) &&
        !string.IsNullOrWhiteSpace(_apiToken);

    public SignalChannelAdapter(
        ILogger<SignalChannelAdapter> logger,
        HttpClient httpClient,
        string? phoneNumber,
        string? serverUrl,
        string? apiToken)
    {
        _logger = logger;
        _httpClient = httpClient;
        _phoneNumber = phoneNumber;
        _serverUrl = serverUrl;
        _apiToken = apiToken;

        if (!IsConfigured)
        {
            _logger.LogWarning("Signal channel is not properly configured");
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
                "Signal channel is not configured",
                retryable: false);
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Recipient phone number is required",
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
            var deliveryResult = await SendMessageWithRetryAsync(recipient, content, ct);
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
            _logger.LogError(ex, "Unexpected error sending Signal message to {Recipient}", recipient);
            return ChannelDeliveryResult.Failed(
                Name,
                $"Unexpected error: {ex.Message}",
                retryable: false);
        }
    }

    private async Task<ChannelDeliveryResult> SendMessageWithRetryAsync(
        string recipient,
        string content,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var deliveryId = await SendDirectAsync(recipient, content, ct);
                _logger.LogInformation(
                    "Successfully sent Signal message to {Recipient} (ID: {DeliveryId}, attempt: {Attempt})",
                    recipient,
                    deliveryId,
                    attempt + 1);

                return ChannelDeliveryResult.Successful(Name, deliveryId);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries - 1)
            {
                var delay = BaseRetryDelaySeconds * (attempt + 1);
                _logger.LogWarning(
                    "Signal delivery attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt + 1,
                    ex.Message,
                    delay);

                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    "Signal delivery failed after {MaxRetries} attempts: {Error}",
                    MaxRetries,
                    ex.Message);
                throw;
            }
        }

        // Should not reach here, but satisfy compiler
        throw new InvalidOperationException("Message delivery retry logic failed");
    }

    private async Task<string> SendDirectAsync(
        string recipient,
        string content,
        CancellationToken ct)
    {
        var url = $"{_serverUrl!.TrimEnd('/')}/v1/send";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "message", content },
                { "number", _phoneNumber! },
                { "recipients", recipient }
            })
        };

        request.Headers.Add("Authorization", $"Bearer {_apiToken}");

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Signal API returned {response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return ExtractMessageId(responseContent);
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
}
