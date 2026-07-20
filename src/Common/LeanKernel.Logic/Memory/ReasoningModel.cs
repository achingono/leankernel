using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Implements <see cref="IReasoningModel"/> using an <see cref="IChatClient"/> with bounded concurrency and timeouts.
/// </summary>
public sealed class ReasoningModel : IReasoningModel
{
    private readonly IChatClient _chatClient;
    private readonly MemorySettings _settings;
    private readonly ILogger<ReasoningModel> _logger;
    private readonly SemaphoreSlim _concurrencyGate;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReasoningModel"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used to execute reasoning prompts.</param>
    /// <param name="settings">The memory reasoning settings.</param>
    /// <param name="logger">The logger used for timeout warnings.</param>
    public ReasoningModel(
        IChatClient chatClient,
        MemorySettings settings,
        ILogger<ReasoningModel> logger)
    {
        _chatClient = chatClient;
        _settings = settings;
        _logger = logger;
        _concurrencyGate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrency));
    }

    /// <inheritdoc />
    public bool Enabled => _settings.Enabled;

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return null;
        }

        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                new ChatOptions
                {
                    MaxOutputTokens = Math.Min(maxOutputTokens, _settings.MaxOutputTokens),
                    Temperature = 0.1f
                },
                linked.Token).ConfigureAwait(false);

            var message = response.Messages?.FirstOrDefault();
            return message?.Text;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Small model call timed out.");
            return null;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }
}