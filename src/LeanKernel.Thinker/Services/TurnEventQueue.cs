using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Durable in-process queue for completed turn events.
/// </summary>
public sealed class TurnEventQueue : ITurnEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Channel<TurnEvent> _channel = Channel.CreateUnbounded<TurnEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly string _queueDirectory;
    private readonly ILogger<TurnEventQueue> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnEventQueue" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration containing queue settings.</param>
    /// <param name="logger">The logger used for queue diagnostics.</param>
    public TurnEventQueue(IOptions<LeanKernelConfig> config, ILogger<TurnEventQueue> logger)
    {
        _logger = logger;
        var leanKernelConfig = config.Value;
        var queuePath = leanKernelConfig.SelfImprovement.QueuePath;
        var dataDirectory = Path.GetDirectoryName(leanKernelConfig.Wiki.BasePath) ?? AppContext.BaseDirectory;
        _queueDirectory = Path.IsPathRooted(queuePath)
            ? queuePath
            : Path.Combine(dataDirectory, queuePath);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        Directory.CreateDirectory(_queueDirectory);
        await File.WriteAllTextAsync(GetEventPath(turnEvent.Id), JsonSerializer.Serialize(turnEvent, JsonOptions), ct);
        await _channel.Writer.WriteAsync(turnEvent, ct);
    }

    /// <summary>
    /// Reads queued turn events as they become available.
    /// </summary>
    /// <param name="ct">A token used to stop reading.</param>
    /// <returns>An asynchronous stream of queued turn events.</returns>
    public async IAsyncEnumerable<TurnEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    /// <summary>
    /// Restores durable queued turn events from disk into the in-process queue.
    /// </summary>
    /// <param name="ct">A token used to cancel restoration.</param>
    public async Task RestorePendingAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_queueDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(_queueDirectory, "*.json").OrderBy(File.GetCreationTimeUtc))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var turnEvent = JsonSerializer.Deserialize<TurnEvent>(json, JsonOptions);
                if (turnEvent is not null)
                    await _channel.Writer.WriteAsync(turnEvent, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore queued turn event from {Path}", file);
            }
        }
    }

    /// <summary>
    /// Marks a turn event as processed by deleting its durable queue file.
    /// </summary>
    /// <param name="turnEventId">The processed turn event identifier.</param>
    /// <param name="ct">A token used to cancel completion work.</param>
    public Task MarkProcessedAsync(string turnEventId, CancellationToken ct)
    {
        var path = GetEventPath(turnEventId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetEventPath(string turnEventId)
    {
        var safeId = string.Join("_", turnEventId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_queueDirectory, $"{safeId}.json");
    }
}
