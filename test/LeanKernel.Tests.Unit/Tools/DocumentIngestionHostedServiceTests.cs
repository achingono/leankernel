using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public sealed class DocumentIngestionHostedServiceTests
{
    [Fact]
    public async Task StartAsync_when_disabled_does_not_process_queued_jobs()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var queue = new DocumentIngestionQueue();
            await using var stream = new MemoryStream("hello"u8.ToArray());
            var job = queue.Queue("report.txt", stream, "Report", []);
            var service = CreateService(tempRoot, queue, enabled: false);

            await service.StartAsync(CancellationToken.None);

            job.Status.Should().Be(DocumentIngestionStatus.Queued);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Hosted_service_processes_stream_jobs_to_completion()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var queue = new DocumentIngestionQueue();
            await using var stream = new MemoryStream("hello from queued stream"u8.ToArray());
            var job = queue.Queue("report.txt", stream, "Report", []);
            var service = CreateService(tempRoot, queue);

            await service.StartAsync(CancellationToken.None);
            await WaitForStatusAsync(job, DocumentIngestionStatus.Completed);
            await StopServiceAsync(service);

            job.Result.Should().NotBeNull();
            job.Result!.PageSlug.Should().Be("doc/report");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Hosted_service_processes_path_jobs_to_completion()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var documentsDir = Path.Combine(tempRoot, "documents", "projects");
            Directory.CreateDirectory(documentsDir);
            var sourcePath = Path.Combine(documentsDir, "watched.txt");
            await File.WriteAllTextAsync(sourcePath, "hello from watched file");

            var queue = new DocumentIngestionQueue();
            var job = queue.QueuePath(sourcePath, null, ["auto-import"]);
            var service = CreateService(tempRoot, queue);

            await service.StartAsync(CancellationToken.None);
            await WaitForStatusAsync(job, DocumentIngestionStatus.Completed);
            await StopServiceAsync(service);

            job.Result.Should().NotBeNull();
            job.Result!.RelativeFilePath.Should().Be("documents/projects/watched.txt");
            File.Exists(sourcePath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Hosted_service_marks_path_jobs_failed_when_source_is_outside_documents_directory()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(tempRoot, "outside.txt");
            await File.WriteAllTextAsync(sourcePath, "outside");
            var queue = new DocumentIngestionQueue();
            var job = queue.QueuePath(sourcePath, null, []);
            var service = CreateService(tempRoot, queue);

            await service.StartAsync(CancellationToken.None);
            await WaitForStatusAsync(job, DocumentIngestionStatus.Failed);
            await StopServiceAsync(service);

            job.ErrorMessage.Should().Contain("documents directory");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static DocumentIngestionHostedService CreateService(
        string allowedRoot,
        DocumentIngestionQueue queue,
        bool enabled = true)
    {
        var config = new LeanKernelConfig
        {
            FileSystem = new FileSystemConfig
            {
                AllowedRoot = allowedRoot,
                MaxExtractedCharacters = 10_000
            },
            DocumentIngestion = new DocumentIngestionConfig
            {
                Enabled = enabled,
                MaxConcurrentJobs = 1,
                ManagedStoragePath = Path.Combine(allowedRoot, "managed-documents")
            }
        };

        var libraryService = new DocumentLibraryService(
            new RecordingKnowledgeService(),
            CreateGBrainClient(),
            Options.Create(config),
            NullLogger<DocumentLibraryService>.Instance);

        return new DocumentIngestionHostedService(
            queue,
            libraryService,
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(config),
            NullLogger<DocumentIngestionHostedService>.Instance);
    }

    private static async Task StopServiceAsync(DocumentIngestionHostedService service)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StopAsync(cts.Token);
    }

    private static async Task WaitForStatusAsync(DocumentIngestionJob job, DocumentIngestionStatus expectedStatus)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (job.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(25);
        }

        job.Status.Should().Be(expectedStatus, job.ErrorMessage);
    }

    private static GBrainMcpClient CreateGBrainClient()
        => new(
            new HttpClient(new RecordingHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new
                    {
                        path = "files/document.txt"
                    }
                })
            }))
            {
                BaseAddress = new Uri("http://localhost")
            },
            NullLogger<GBrainMcpClient>.Instance);

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lk-ingestion-hosted-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<RetrievalCandidate>)[]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePageAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
