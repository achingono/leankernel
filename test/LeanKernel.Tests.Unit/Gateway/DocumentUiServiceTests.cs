using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using LeanKernel.Knowledge;
using LeanKernel.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Gateway;

public sealed class DocumentUiServiceTests
{
    [Fact]
    public async Task BrowseDocumentsAsync_maps_gbrain_document_pages()
    {
        var service = CreateService(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                pages = new[]
                {
                    new
                    {
                        slug = "doc/report",
                        updated_at = "2026-05-28T00:00:00Z",
                        tags = new[] { "auto-import", "report" }
                    }
                }
            }
        });

        var documents = await service.BrowseDocumentsAsync();

        documents.Should().ContainSingle();
        documents[0].Slug.Should().Be("doc/report");
        documents[0].TagCount.Should().Be(2);
        documents[0].Tags.Should().Contain(["auto-import", "report"]);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_gbrain_file_url()
    {
        var service = CreateService(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                url = "/files/report.txt"
            }
        });

        var url = await service.GetDownloadUrlAsync("files/report.txt");

        url.Should().Be("/files/report.txt");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_returns_fallback_when_gbrain_result_has_no_url()
    {
        var service = CreateService(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { }
        });

        var url = await service.GetDownloadUrlAsync("files/report.txt");

        url.Should().Be("/api/files/download?path=files%2Freport.txt");
    }

    [Fact]
    public async Task IngestDocumentAsync_forwards_to_document_library_service()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lk-doc-ui-ingest-tests-{Guid.NewGuid():N}");
        try
        {
            var service = CreateService(
                new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new
                    {
                        path = "files/report.txt"
                    }
                },
                tempRoot);
            await using var stream = new MemoryStream("hello from ui upload"u8.ToArray());

            var result = await service.IngestDocumentAsync("report.txt", stream, "Report", ["ui"]);

            result.PageSlug.Should().Be("doc/report");
            result.FileStoragePath.Should().Be("files/report.txt");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static DocumentUiService CreateService(object gbrainResponse, string? allowedRoot = null)
    {
        var tempRoot = allowedRoot ?? Path.Combine(Path.GetTempPath(), $"lk-doc-ui-tests-{Guid.NewGuid():N}");
        var config = new LeanKernelConfig
        {
            FileSystem = new FileSystemConfig
            {
                AllowedRoot = tempRoot
            },
            DocumentIngestion = new DocumentIngestionConfig
            {
                ManagedStoragePath = Path.Combine(tempRoot, "managed-documents")
            }
        };

        var gbrainClient = new GBrainMcpClient(
            new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(gbrainResponse)
            }))
            {
                BaseAddress = new Uri("http://localhost")
            },
            NullLogger<GBrainMcpClient>.Instance);

        return new DocumentUiService(
            new DocumentLibraryService(
                new EmptyKnowledgeService(),
                gbrainClient,
                Options.Create(config),
                NullLogger<DocumentLibraryService>.Instance),
            gbrainClient,
            NullLogger<DocumentUiService>.Instance);
    }

    private sealed class EmptyKnowledgeService : IKnowledgeService
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

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
