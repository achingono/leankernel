using LeanKernel.Abstractions.Models;
using LeanKernel.Tools;

namespace LeanKernel.Tests.Unit.Tools;

public class DocumentIngestionQueueTests
{
    [Fact]
    public void Queue_creates_job_with_queued_status()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        var job = queue.Queue("test.txt", stream, "title", ["tag1"]);

        // Assert
        Assert.NotNull(job);
        Assert.False(string.IsNullOrEmpty(job.JobId));
        Assert.Equal("test.txt", job.Filename);
        Assert.Equal("title", job.Title);
        Assert.Single(job.Tags);
        Assert.Equal("tag1", job.Tags[0]);
        Assert.Equal(DocumentIngestionStatus.Queued, job.Status);
    }

    [Fact]
    public void Queue_returns_job_that_can_be_retrieved()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        var queuedJob = queue.Queue("test.txt", stream, "title", ["tag1"]);
        var retrievedJob = queue.GetJobStatus(queuedJob.JobId);

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal(queuedJob.JobId, retrievedJob.JobId);
    }

    [Fact]
    public void GetJobStatus_returns_null_for_unknown_job_id()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);

        // Act
        var job = queue.GetJobStatus("unknown-job-id");

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public void PendingCount_returns_zero_initially()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);

        // Act
        var count = queue.PendingCount;

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void PendingCount_increments_when_job_queued()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        queue.Queue("test.txt", stream, null, []);
        var count = queue.PendingCount;

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void Queue_with_null_title_creates_job_with_null_title()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        var job = queue.Queue("test.txt", stream, null, []);

        // Assert
        Assert.Null(job.Title);
    }

    [Fact]
    public void Queue_with_empty_tags_creates_job_with_empty_tags()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act
        var job = queue.Queue("test.txt", stream, null, []);

        // Assert
        Assert.Empty(job.Tags);
    }

    [Fact]
    public void Queue_throws_when_max_capacity_exceeded()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 1);
        using var stream1 = new MemoryStream([1, 2, 3]);
        using var stream2 = new MemoryStream([1, 2, 3]);

        // Act - queue first job (should succeed)
        var job1 = queue.Queue("test1.txt", stream1, null, []);

        // Act - queue second job with full queue (should throw)
        var ex = Assert.Throws<InvalidOperationException>(() =>
            queue.Queue("test2.txt", stream2, null, [])
        );

        // Assert
        Assert.Contains("queue is full", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Queue_creates_multiple_jobs_with_unique_ids()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream1 = new MemoryStream([1, 2, 3]);
        using var stream2 = new MemoryStream([4, 5, 6]);

        // Act
        var job1 = queue.Queue("test1.txt", stream1, null, []);
        var job2 = queue.Queue("test2.txt", stream2, null, []);

        // Assert
        Assert.NotEqual(job1.JobId, job2.JobId);
    }

    [Fact]
    public void Queue_throws_on_empty_filename()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            queue.Queue("", stream, null, [])
        );
    }

    [Fact]
    public void Queue_throws_on_null_stream()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            queue.Queue("test.txt", null!, null, [])
        );
    }

    [Fact]
    public void Queue_throws_on_null_tags()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            queue.Queue("test.txt", stream, null, null!)
        );
    }

    [Fact]
    public void GetJobStatus_throws_on_empty_job_id()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            queue.GetJobStatus("")
        );
    }

    [Fact]
    public void Queue_with_multiple_tags()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        using var stream = new MemoryStream([1, 2, 3]);
        var tags = new List<string> { "tag1", "tag2", "tag3" };

        // Act
        var job = queue.Queue("test.txt", stream, "title", tags);

        // Assert
        Assert.Equal(3, job.Tags.Count);
        Assert.Contains("tag1", job.Tags);
        Assert.Contains("tag2", job.Tags);
        Assert.Contains("tag3", job.Tags);
    }

    [Fact]
    public void QueuePath_creates_path_job_with_queued_status()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);
        var sourcePath = Path.Combine(Path.GetTempPath(), "report.txt");

        // Act
        var job = queue.QueuePath(sourcePath, "Report", ["auto-import"]);

        // Assert
        Assert.NotNull(job);
        Assert.Equal("report.txt", job.Filename);
        Assert.Equal(sourcePath, job.SourcePath);
        Assert.Equal("Report", job.Title);
        Assert.Single(job.Tags);
        Assert.Equal("auto-import", job.Tags[0]);
        Assert.Equal(DocumentIngestionStatus.Queued, job.Status);
        Assert.Null(job.FileContent);
    }

    [Fact]
    public void QueuePath_throws_on_empty_source_path()
    {
        // Arrange
        var queue = new DocumentIngestionQueue(maxQueuedJobs: 100);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            queue.QueuePath("", null, [])
        );
    }
}
