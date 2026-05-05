using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services.Channels.Adapters;

/// <summary>
/// Signal Messenger channel adapter for sending messages via signal-cli.
/// Uses the local signal-cli command with a pre-configured Signal account.
/// </summary>
public sealed class SignalChannelAdapter : IMessageChannel
{
    private readonly ILogger<SignalChannelAdapter> _logger;
    private readonly bool _isEnabled;
    private readonly string? _cliPath;
    private readonly string? _account;
    private const int MaxRetries = 3;
    private const int BaseRetryDelaySeconds = 2;

    public string Name => "Signal";

    public bool IsConfigured =>
        _isEnabled &&
        !string.IsNullOrWhiteSpace(_cliPath) &&
        !string.IsNullOrWhiteSpace(_account);

    public SignalChannelAdapter(
        ILogger<SignalChannelAdapter> logger,
        string? cliPath,
        string? account,
        bool isEnabled = true)
    {
        _logger = logger;
        _isEnabled = isEnabled;
        _cliPath = cliPath;
        _account = account;

        if (_isEnabled && !IsConfigured)
        {
            _logger.LogWarning("Signal channel is not properly configured");
        }
    }

    public async Task<ChannelDeliveryResult> DeliverAsync(
        string recipient,
        string content,
        CancellationToken ct = default)
    {
        if (!_isEnabled)
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Signal channel is disabled",
                retryable: false);
        }

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
            var deliveryId = await SendWithRetryAsync(recipient, content, ct);
            return ChannelDeliveryResult.Successful(Name, deliveryId);
        }
        catch (OperationCanceledException)
        {
            return ChannelDeliveryResult.Failed(
                Name,
                "Message delivery was cancelled",
                retryable: true,
                TimeSpan.FromSeconds(BaseRetryDelaySeconds * 2));
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

    private async Task<string> SendWithRetryAsync(
        string recipient,
        string content,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var messageId = await InvokeSignalCliAsync(recipient, content, ct);
                _logger.LogInformation(
                    "Successfully sent Signal message to {Recipient} (ID: {MessageId}, attempt: {Attempt})",
                    recipient,
                    messageId,
                    attempt + 1);

                return messageId;
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                var delay = BaseRetryDelaySeconds * (attempt + 1);
                _logger.LogWarning(
                    "Signal delivery attempt {Attempt} failed: {Error}. Retrying in {Delay}s",
                    attempt + 1,
                    ex.Message,
                    delay);

                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Signal delivery failed after {MaxRetries} attempts: {Error}",
                    MaxRetries,
                    ex.Message);
                throw;
            }
        }

        throw new InvalidOperationException("Message delivery retry logic failed");
    }

    private async Task<string> InvokeSignalCliAsync(
        string recipient,
        string content,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = $"--account {_account} send -m \"{EscapeShellArg(content)}\" {recipient}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start signal-cli process at {_cliPath}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            throw new TimeoutException("signal-cli command timed out after 30 seconds");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"signal-cli command failed with exit code {process.ExitCode}: {error}");
        }

        // Return a generated message ID (signal-cli doesn't provide one in standard output)
        return Guid.NewGuid().ToString();
    }

    private static string EscapeShellArg(string arg)
    {
        // Escape double quotes and backslashes for shell safety
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
