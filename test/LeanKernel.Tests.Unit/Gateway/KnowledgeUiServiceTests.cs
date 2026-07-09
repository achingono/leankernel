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
using Moq;

namespace LeanKernel.Tests.Unit.Gateway;

public class KnowledgeUiServiceTests
{
    private readonly Mock<IKnowledgeService> _knowledgeServiceMock = new();
    private readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    public KnowledgeUiServiceTests()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>());
    }

    private KnowledgeUiService CreateService()
        => new(
            _knowledgeServiceMock.Object,
            NullLogger<KnowledgeUiService>.Instance,
            _emptyServiceProvider);

    [Fact]
    public async Task SearchPagesAsync_returns_empty_when_query_is_empty()
    {
        var service = CreateService();
        var results = await service.SearchPagesAsync("");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPagesAsync_returns_empty_when_query_is_whitespace()
    {
        var service = CreateService();
        var results = await service.SearchPagesAsync("   ");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPagesAsync_maps_results_from_knowledge_service()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("test query", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "page-one",
                    Content = "This is the content of page one.",
                    Source = "wiki",
                    Score = 0.95,
                    TokenCount = 50,
                },
                new()
                {
                    Key = "page-two",
                    Content = "Short",
                    Source = "wiki",
                    Score = 0.80,
                    TokenCount = 10,
                },
            });

        var service = CreateService();
        var results = await service.SearchPagesAsync("test query");

        results.Should().HaveCount(2);
        results[0].Slug.Should().Be("page-one");
        results[0].Score.Should().Be(0.95);
        results[1].Slug.Should().Be("page-two");
        results[1].Score.Should().Be(0.80);
    }

    [Fact]
    public async Task SearchPagesAsync_previews_are_truncated_to_200_characters()
    {
        var longContent = new string('x', 500);
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("long", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "long-page",
                    Content = longContent,
                    Source = "wiki",
                    Score = 1.0,
                    TokenCount = 100,
                },
            });

        var service = CreateService();
        var results = await service.SearchPagesAsync("long");

        results[0].Preview.Should().HaveLength(201); // 200 chars + ellipsis
        results[0].Preview.Should().EndWith("…");
    }

    [Fact]
    public async Task SearchPagesAsync_handles_empty_results()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("query", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>());

        var service = CreateService();
        var results = await service.SearchPagesAsync("query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SavePageAsync_calls_PutPageAsync_with_slug_and_content()
    {
        var service = CreateService();
        await service.SavePageAsync(" my-page ", "content here");

        _knowledgeServiceMock.Verify(
            s => s.PutPageAsync("my-page", "content here", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SavePageAsync_throws_on_null_content()
    {
        var service = CreateService();
        var act = async () => await service.SavePageAsync("slug", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SavePageAsync_throws_on_empty_slug()
    {
        var service = CreateService();
        var act = async () => await service.SavePageAsync("", "content");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPageDetailAsync_returns_null_when_page_not_found()
    {
        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgePage?)null);

        var service = CreateService();
        var result = await service.GetPageDetailAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPageDetailAsync_returns_detail_for_existing_page()
    {
        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Some content",
                LastModified = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                LinkedPages = ["related-page"],
            });

        var service = CreateService();
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("existing");
        result.Content.Should().Be("Some content");
        result.LastModified.Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        result.LinkedPages.Should().ContainSingle("related-page");
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageDetailAsync_throws_on_empty_slug()
    {
        var service = CreateService();
        var act = async () => await service.GetPageDetailAsync(" ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BrowsePagesAsync_uses_fallback_search_when_mcp_client_unavailable()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "wiki/intro",
                    Content = "Intro page",
                    Source = "gbrain",
                    Score = 0.9,
                    TokenCount = 5,
                },
            });

        var service = CreateService();
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(item => item.Slug == "wiki/intro");
    }

    [Fact]
    public async Task BrowsePagesAsync_normalizes_page_number_less_than_one()
    {
        var service = CreateService();
        var result = await service.BrowsePagesAsync(0, 10, KnowledgeBrowseSort.RecentlyModified);

        result.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task BrowsePagesAsync_clamps_page_size_to_maximum_of_fifty()
    {
        var service = CreateService();
        var result = await service.BrowsePagesAsync(1, 100, KnowledgeBrowseSort.RecentlyModified);

        result.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task BrowsePagesAsync_uses_alphabetical_sort_when_specified()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "zeta-page",
                    Content = "Zeta",
                    Source = "gbrain",
                    Score = 0.5,
                    TokenCount = 1,
                },
                new()
                {
                    Key = "alpha-page",
                    Content = "Alpha",
                    Source = "gbrain",
                    Score = 0.5,
                    TokenCount = 1,
                },
            });

        var service = CreateService();
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.Alphabetical);

        result.Items[0].Slug.Should().Be("alpha-page");
        result.Items[1].Slug.Should().Be("zeta-page");
    }

    // =========================================================================
    // Helpers for GBrain-backed tests
    // =========================================================================

    private static KnowledgeUiService CreateServiceWithGBrain(
        IKnowledgeService knowledgeService,
        HttpMessageHandler handler)
    {
        var client = new GBrainMcpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(client);

        return new KnowledgeUiService(
            knowledgeService,
            NullLogger<KnowledgeUiService>.Instance,
            services.BuildServiceProvider());
    }

    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public QueuedHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responses.Dequeue());
    }

    // =========================================================================
    // BrowsePagesAsync with GBrain
    // =========================================================================

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_supported_returns_items()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { slug = "page-1", updated_at = (string?)"2026-06-01T12:00:00Z", tags = Array.Empty<string>() },
                new { slug = "page-2", updated_at = (string?)"2026-06-02T12:00:00Z", tags = Array.Empty<string>() },
                new { slug = "page-3", updated_at = (string?)null, tags = Array.Empty<string>() }
            },
            total_count = 3
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeFalse();
        result.Items.Should().HaveCount(3);
        result.Items[0].Slug.Should().Be("page-1");
        result.Items[1].Slug.Should().Be("page-2");
        result.Items[2].Slug.Should().Be("page-3");
        result.TotalCount.Should().Be(3);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_not_supported_falls_back_to_discovered()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "discovered-page",
                    Content = "Discovered via wiki search",
                    Source = "gbrain",
                    Score = 0.8,
                    TokenCount = 5
                }
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "search" } } }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(i => i.Slug == "discovered-page");
        result.StatusMessage.Should().Contain("does not expose page listing");
    }

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_returns_empty_items_falls_back()
    {
        var emptyBrowseJson = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            total_count = 0
        });

        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "fallback-page",
                    Content = "Fallback content",
                    Source = "gbrain",
                    Score = 0.7,
                    TokenCount = 3
                }
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = emptyBrowseJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(i => i.Slug == "fallback-page");
        result.StatusMessage.Should().Contain("returned no pages");
    }

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_returns_null_returns_empty()
    {
        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = (object?)null
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeFalse();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.StatusMessage.Should().Be("No knowledge pages were returned by the provider.");
    }

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_throws_GBrainException_falls_back()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "recovered-page",
                    Content = "Recovered from GBrain error",
                    Source = "gbrain",
                    Score = 0.6,
                    TokenCount = 4
                }
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    error = new { code = -32603, message = "Internal server error" }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(i => i.Slug == "recovered-page");
    }

    [Fact]
    public async Task BrowsePagesAsync_when_list_pages_throws_HttpRequestException_falls_back()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("wiki", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "http-error-recovery",
                    Content = "Recovered",
                    Source = "gbrain",
                    Score = 0.5,
                    TokenCount = 2
                }
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.IsDegraded.Should().BeTrue();
        result.Items.Should().ContainSingle(i => i.Slug == "http-error-recovery");
    }

    [Fact]
    public async Task BrowsePagesAsync_parses_total_count_from_nested_pagination()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            items = new[] { new { slug = "page-1" } },
            pagination = new { total = 42 }
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.TotalCount.Should().Be(42);
    }

    [Fact]
    public async Task BrowsePagesAsync_parses_page_summary_with_tags()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { slug = "tagged-page", tag_count = 3, tags = new[] { "alpha", "beta", "gamma" } },
                new { slug = "untagged-page", tag_count = 0, tags = Array.Empty<string>() }
            },
            total_count = 2
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        var tagged = result.Items.Should().ContainSingle(i => i.Slug == "tagged-page").Subject;
        tagged.TagCount.Should().Be(3);
        tagged.Tags.Should().BeEquivalentTo("alpha", "beta", "gamma");

        var untagged = result.Items.Should().ContainSingle(i => i.Slug == "untagged-page").Subject;
        untagged.TagCount.Should().Be(0);
        untagged.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task BrowsePagesAsync_uses_alphabetical_sort_with_gbrain()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { slug = "beta-page", updated_at = "2026-06-01T12:00:00Z" },
                new { slug = "alpha-page", updated_at = "2026-06-02T12:00:00Z" }
            },
            total_count = 2
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.Alphabetical);

        result.IsDegraded.Should().BeFalse();
        result.Items.Should().HaveCount(2);
        result.Sort.Should().Be(KnowledgeBrowseSort.Alphabetical);
    }

    [Fact]
    public async Task BrowsePagesAsync_supports_second_page_with_gbrain()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            items = new[] { new { slug = "page-11" } },
            total_count = 25
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(2, 10, KnowledgeBrowseSort.RecentlyModified);

        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.Items.Should().ContainSingle(i => i.Slug == "page-11");
        result.TotalCount.Should().Be(25);
    }

    [Fact]
    public async Task BrowsePagesAsync_parses_items_from_items_array_in_data_wrapper()
    {
        var browseResultJson = JsonSerializer.Serialize(new
        {
            data = new
            {
                items = new[] { new { slug = "data-wrapped-page" } },
                total_count = 1
            }
        });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new { tools = new[] { new { name = "list_pages" } } }
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 2,
                    result = new
                    {
                        content = new[] { new { type = "text", text = browseResultJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        result.Items.Should().ContainSingle(i => i.Slug == "data-wrapped-page");
    }

    // =========================================================================
    // GetPageDetailAsync with GBrain
    // =========================================================================

    [Fact]
    public async Task GetPageDetailAsync_enriches_with_gbrain_metadata()
    {
        var enrichedJson = JsonSerializer.Serialize(new
        {
            slug = "existing",
            compiled_truth = "Enriched content body",
            updated_at = "2026-06-15T12:00:00Z",
            tags = new[] { "tag1", "tag2" },
            links = new[] { new { slug = "link1" }, new { slug = "link2" } }
        });

        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Base content",
                LastModified = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                LinkedPages = ["related-page"]
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new
                    {
                        content = new[] { new { type = "text", text = enrichedJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("existing");
        result.Content.Should().Be("Enriched content body");
        result.LastModified.Should().Be(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        result.Tags.Should().BeEquivalentTo("tag1", "tag2");
        result.LinkedPages.Should().BeEquivalentTo("link1", "link2");
    }

    [Fact]
    public async Task GetPageDetailAsync_preserves_base_content_when_enrichment_has_empty_content()
    {
        var enrichedJson = JsonSerializer.Serialize(new
        {
            slug = "existing",
            compiled_truth = ""
        });

        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Base content",
                LastModified = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                LinkedPages = ["related-page"]
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new
                    {
                        content = new[] { new { type = "text", text = enrichedJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Content.Should().Be("Base content");
        result.LastModified.Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        result.Tags.Should().BeEmpty();
        result.LinkedPages.Should().BeEquivalentTo("related-page");
    }

    [Fact]
    public async Task GetPageDetailAsync_uses_enriched_slug_when_different()
    {
        var enrichedJson = JsonSerializer.Serialize(new
        {
            slug = "different-slug"
        });

        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Content",
                LastModified = null,
                LinkedPages = null
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    result = new
                    {
                        content = new[] { new { type = "text", text = enrichedJson } },
                        isError = false
                    }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("different-slug");
    }

    [Fact]
    public async Task GetPageDetailAsync_when_gbrain_throws_GBrainException_returns_base_detail()
    {
        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Base content",
                LastModified = null,
                LinkedPages = null
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0", id = 1,
                    error = new { code = -32603, message = "GBrain unavailable" }
                })
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("existing");
        result.Content.Should().Be("Base content");
    }

    [Fact]
    public async Task GetPageDetailAsync_when_gbrain_throws_HttpRequestException_returns_base_detail()
    {
        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Base content",
                LastModified = null,
                LinkedPages = null
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("existing");
        result.Content.Should().Be("Base content");
    }

    [Fact]
    public async Task GetPageDetailAsync_when_gbrain_throws_JsonException_returns_base_detail()
    {
        _knowledgeServiceMock
            .Setup(s => s.GetPageAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "existing",
                Content = "Base content",
                LastModified = null,
                LinkedPages = null
            });

        var handler = new QueuedHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not valid json", System.Text.Encoding.UTF8, "application/json")
            });

        var service = CreateServiceWithGBrain(_knowledgeServiceMock.Object, handler);
        var result = await service.GetPageDetailAsync("existing");

        result.Should().NotBeNull();
        result!.Slug.Should().Be("existing");
        result.Content.Should().Be("Base content");
    }

    // =========================================================================
    // SearchPagesAsync — metadata mapping
    // =========================================================================

    [Fact]
    public async Task SearchPagesAsync_maps_metadata_updated_at_to_last_modified()
    {
        _knowledgeServiceMock
            .Setup(s => s.SearchAsync("query", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalCandidate>
            {
                new()
                {
                    Key = "dated-page",
                    Content = "Content",
                    Source = "wiki",
                    Score = 0.9,
                    TokenCount = 5,
                    Metadata = new Dictionary<string, string>
                    {
                        ["updated_at"] = "2026-06-15T10:30:00Z"
                    }
                }
            });

        var service = CreateService();
        await service.SearchPagesAsync("query");

        // Browse fallback reads from the _knownPages populated by SearchPagesAsync
        var browseResult = await service.BrowsePagesAsync(1, 10, KnowledgeBrowseSort.RecentlyModified);

        browseResult.Items.Should().ContainSingle(i => i.Slug == "dated-page");
        var item = browseResult.Items.First(i => i.Slug == "dated-page");
        item.LastModified.Should().Be(new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero));
    }
}
