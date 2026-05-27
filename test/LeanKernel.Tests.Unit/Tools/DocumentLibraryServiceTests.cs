using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Tools;

public class DocumentLibraryServiceTests
{
    [Fact]
    public async Task IngestDocumentAsync_generates_unique_slug_and_persists_storage_path_metadata()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var knowledgeService = new RecordingKnowledgeService(existingPages: ["doc/report"]);
            var gBrainClient = CreateGBrainClient(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    path = "files/report-asset.txt"
                }
            })));
            var service = CreateService(tempRoot, knowledgeService, gBrainClient);

            await using var contentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello from doc"));

            var result = await service.IngestDocumentAsync("report.txt", contentStream, "Report", []);

            result.PageSlug.Should().Be("doc/report-2");
            knowledgeService.PutCalls.Should().HaveCount(2);
            knowledgeService.PutCalls[^1].Content.Should().Contain("storage_path: \"files/report-asset.txt\"");
            knowledgeService.PutCalls[^1].Content.Should().Contain("source_file: \"documents/report-");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task IngestDocumentAsync_throws_and_cleans_local_file_when_upload_fails()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var knowledgeService = new RecordingKnowledgeService();
            var gBrainClient = CreateGBrainClient(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 1,
                error = new { code = 500, message = "upload failed" }
            })));
            var service = CreateService(tempRoot, knowledgeService, gBrainClient);

            await using var contentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello from doc"));

            var act = () => service.IngestDocumentAsync("report.txt", contentStream, "Report", []);

            await act.Should().ThrowAsync<GBrainException>();
            Directory.GetFiles(Path.Combine(tempRoot, "documents")).Should().BeEmpty();
            knowledgeService.DeleteCalls.Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task IngestDocumentAsync_cleans_wiki_page_when_second_write_fails()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var knowledgeService = new FailOnSecondWriteKnowledgeService();
            var gBrainClient = CreateGBrainClient(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    path = "files/report-asset.txt"
                }
            })));
            var service = CreateService(tempRoot, knowledgeService, gBrainClient);

            await using var contentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello from doc"));

            var act = () => service.IngestDocumentAsync("report.txt", contentStream, "Report", []);

            await act.Should().ThrowAsync<InvalidOperationException>();
            knowledgeService.DeleteCalls.Should().HaveCount(1);
            knowledgeService.DeleteCalls.First().Should().Contain("/report");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lk-doclib-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static DocumentLibraryService CreateService(
        string allowedRoot,
        IKnowledgeService knowledgeService,
        GBrainMcpClient gBrainClient)
    {
        var config = new LeanKernelConfig
        {
            FileSystem = new FileSystemConfig
            {
                AllowedRoot = allowedRoot,
                MaxExtractedCharacters = 10_000
            }
        };

        return new DocumentLibraryService(
            knowledgeService,
            gBrainClient,
            Options.Create(config),
            NullLogger<DocumentLibraryService>.Instance);
    }

    private static GBrainMcpClient CreateGBrainClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class RecordingKnowledgeService(IReadOnlyCollection<string>? existingPages = null) : IKnowledgeService
    {
        private readonly HashSet<string> _existingPages = new(existingPages ?? [], StringComparer.OrdinalIgnoreCase);

        public List<(string Key, string Content)> PutCalls { get; } = [];

        public List<string> DeleteCalls { get; } = [];

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<RetrievalCandidate>)[]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult(
                _existingPages.Contains(key)
                    ? new KnowledgePage
                    {
                        Key = key,
                        Content = "existing",
                        LastModified = DateTimeOffset.UtcNow,
                        LinkedPages = []
                    }
                    : null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
        {
            PutCalls.Add((key, content));
            _existingPages.Add(key);
            return Task.CompletedTask;
        }

        public Task DeletePageAsync(string key, CancellationToken ct = default)
        {
            DeleteCalls.Add(key);
            _existingPages.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnSecondWriteKnowledgeService : IKnowledgeService
    {
        private int _writeCount = 0;

        public List<string> DeleteCalls { get; } = [];

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<RetrievalCandidate>)[]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
        {
            _writeCount++;
            if (_writeCount == 2)
            {
                throw new InvalidOperationException("Simulated second write failure");
            }
            return Task.CompletedTask;
        }

        public Task DeletePageAsync(string key, CancellationToken ct = default)
        {
            DeleteCalls.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return _handler(request, cancellationToken);
        }
    }
}
