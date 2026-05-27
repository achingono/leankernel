using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using LeanKernel.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Gateway;

public class KnowledgeUiServiceTests
{
    [Fact]
    public async Task BrowsePagesAsync_discovers_pages_from_search_when_listing_is_unavailable()
    {
        var knowledgeService = new RecordingKnowledgeService
        {
            SearchResponses =
            {
                ["wiki"] =
                [
                    new RetrievalCandidate
                    {
                        Key = "wiki/session-fact-1",
                        Content = "Fact 1",
                        Source = "gbrain",
                        Score = 0.8,
                        TokenCount = 2
                    }
                ]
            }
        };
        var uiService = new KnowledgeUiService(
            knowledgeService,
            NullLogger<KnowledgeUiService>.Instance,
            new ServiceCollection().BuildServiceProvider());

        var result = await uiService.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.Slug == "wiki/session-fact-1");
    }

    [Fact]
    public async Task BrowsePagesAsync_parses_list_pages_with_path_and_nested_pagination_total()
    {
        var knowledgeService = new RecordingKnowledgeService();
        var handler = new SequencedHttpMessageHandler(
        [
            CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    tools = new[]
                    {
                        new { name = "list_pages", description = "List pages" }
                    }
                }
            }),
            CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 2,
                result = new
                {
                    items = new[]
                    {
                        new
                        {
                            path = "wiki/path-based-page",
                            updated_at = "2026-05-25T00:00:00Z",
                            tag_count = 2
                        }
                    },
                    pagination = new
                    {
                        total = 1
                    }
                }
            })
        ]);

        var gBrainClient = new GBrainMcpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);
        var serviceProvider = new ServiceCollection()
            .AddSingleton(gBrainClient)
            .BuildServiceProvider();

        var uiService = new KnowledgeUiService(
            knowledgeService,
            NullLogger<KnowledgeUiService>.Instance,
            serviceProvider);

        var result = await uiService.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeFalse();
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.Slug == "wiki/path-based-page");
    }

    [Fact]
    public async Task BrowsePagesAsync_falls_back_to_search_when_list_pages_returns_empty()
    {
        var knowledgeService = new RecordingKnowledgeService
        {
            SearchResponses =
            {
                ["wiki"] =
                [
                    new RetrievalCandidate
                    {
                        Key = "learning/facts/session-1/turn-1/01",
                        Content = "Extracted fact",
                        Source = "gbrain",
                        Score = 0.9,
                        TokenCount = 3
                    }
                ]
            }
        };

        var handler = new SequencedHttpMessageHandler(
        [
            CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    tools = new[]
                    {
                        new { name = "list_pages", description = "List pages" }
                    }
                }
            }),
            CreateJsonResponse(new
            {
                jsonrpc = "2.0",
                id = 2,
                result = new
                {
                    items = Array.Empty<object>(),
                    total_count = 0
                }
            })
        ]);

        var gBrainClient = new GBrainMcpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);
        var serviceProvider = new ServiceCollection()
            .AddSingleton(gBrainClient)
            .BuildServiceProvider();

        var uiService = new KnowledgeUiService(
            knowledgeService,
            NullLogger<KnowledgeUiService>.Instance,
            serviceProvider);

        var result = await uiService.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.Slug == "learning/facts/session-1/turn-1/01");
    }

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Dictionary<string, IReadOnlyList<RetrievalCandidate>> SearchResponses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult(
                SearchResponses.TryGetValue(query, out var results)
                    ? results
                    : (IReadOnlyList<RetrievalCandidate>)[]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePageAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class SequencedHttpMessageHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responses.Count > 1
                ? _responses.Dequeue()
                : _responses.Peek();
            return Task.FromResult(response);
        }
    }
}
