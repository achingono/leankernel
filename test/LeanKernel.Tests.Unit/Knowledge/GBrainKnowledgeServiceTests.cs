using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Knowledge;

public class GBrainKnowledgeServiceTests
{
    [Fact]
    public async Task SearchAsync_maps_results_to_retrieval_candidates()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                results = new[]
                {
                    new
                    {
                        slug = "doc-1",
                        compiled_truth = "12345678",
                        score = 0.85,
                        page_id = 7
                    }
                }
            }
        }));
        var service = CreateService(handler);

        var results = await service.SearchAsync("atlas", 2);

        results.Should().ContainSingle();
        results[0].Key.Should().Be("doc-1");
        results[0].Source.Should().Be("gbrain");
        results[0].Score.Should().Be(0.85);
        results[0].TokenCount.Should().Be(2);
        results[0].Metadata!["page_id"].Should().Be("7");
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("search");
        document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("limit").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_when_the_mcp_result_has_no_results()
    {
        var service = CreateService(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { }
        })));

        var results = await service.SearchAsync("atlas");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_returns_a_knowledge_page()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                slug = "wiki/docs",
                compiled_truth = "# Docs",
                updated_at = "2025-05-20T10:00:00Z",
                tags = new[] { "docs" },
                links = new[]
                {
                    new
                    {
                        to_slug = "wiki/home",
                        link_type = "references"
                    }
                }
            }
        }));
        var service = CreateService(handler);

        var page = await service.GetPageAsync("wiki/docs");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/docs");
        page.Content.Should().Be("# Docs");
        page.LinkedPages.Should().Equal("wiki/home");
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("get_page");
        document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("slug").GetString().Should().Be("wiki/docs");
    }

    [Fact]
    public async Task GetPageAsync_returns_null_when_the_page_is_missing()
    {
        var service = CreateService(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            error = new { code = 404, message = "page not found" }
        })));

        var page = await service.GetPageAsync("wiki/missing");

        page.Should().BeNull();
    }

    [Fact]
    public async Task PutPageAsync_calls_the_put_page_tool()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { }
        }));
        var service = CreateService(handler);

        await service.PutPageAsync("wiki/docs", "# Docs");

        handler.RequestBodies.Should().ContainSingle();
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("put_page");
        document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("slug").GetString().Should().Be("wiki/docs");
        document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("content").GetString().Should().Be("# Docs");
    }

    [Fact]
    public void AddLeanKernelKnowledge_registers_the_client_and_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLeanKernelKnowledge(new GBrainConfig
        {
            BaseUrl = "http://localhost:8789",
            TimeoutSeconds = 5,
            AuthToken = "token"
        });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GBrainMcpClient>().Should().NotBeNull();
        provider.GetRequiredService<IKnowledgeService>().Should().BeOfType<GBrainKnowledgeService>();
    }

    private static GBrainKnowledgeService CreateService(HttpMessageHandler handler)
    {
        var client = new GBrainMcpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);

        return new GBrainKnowledgeService(client, NullLogger<GBrainKnowledgeService>.Instance);
    }

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

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
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            return _handler(request, cancellationToken);
        }
    }
}
