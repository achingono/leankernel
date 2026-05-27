using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LeanKernel.Tools;

/// <summary>
/// Provides an in-memory queue for document ingestion jobs.
/// </summary>
public sealed class DocumentIngestionQueue : IDocumentIngestionQueue
{
    private readonly Channel<DocumentIngestionJob> _channel;
    private readonly ConcurrentDictionary<string, DocumentIngestionJob> _jobs = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentIngestionQueue"/> class.
    /// </summary>
    /// <param name="maxQueuedJobs">Maximum number of jobs in the queue before blocking enqueue.</param>
    public DocumentIngestionQueue(int maxQueuedJobs = 100)
    {
        var options = new BoundedChannelOptions(maxQueuedJobs)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<DocumentIngestionJob>(options);
    }

    /// <inheritdoc />
    public DocumentIngestionJob Queue(
        string filename,
        Stream fileContent,
        string? title,
        List<string> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentNullException.ThrowIfNull(tags);

        var job = new DocumentIngestionJob
        {
            Filename = filename,
            FileContent = fileContent,
            Title = title,
            Tags = tags,
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _jobs.TryAdd(job.JobId, job);
        var enqueued = _channel.Writer.TryWrite(job);
        if (!enqueued)
        {
            _jobs.TryRemove(job.JobId, out _);
            throw new InvalidOperationException($"Document ingestion queue is full. Queue size limit: {_channel.Reader.Count}");
        }

        return job;
    }

    /// <inheritdoc />
    public DocumentIngestionJob? GetJobStatus(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    /// <inheritdoc />
    public int PendingCount => _channel.Reader.Count;

    /// <summary>
    /// Gets the internal channel reader for consuming jobs.
    /// </summary>
    internal ChannelReader<DocumentIngestionJob> Reader => _channel.Reader;
}
