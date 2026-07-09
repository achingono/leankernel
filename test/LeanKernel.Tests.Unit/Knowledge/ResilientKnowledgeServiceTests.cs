using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Knowledge.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Knowledge;

public class ResilientKnowledgeServiceTests
{
    // ---------------------------------------------------------------
    // SearchAsync
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_throws_ArgumentException_on_null_or_whitespace_query(string? query)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SearchAsync(query!));
    }

    [Fact]
    public async Task SearchAsync_healthy_provider_returns_results_from_inner_service()
    {
        var handler = RespondWith(McpSuccess("search", SearchResultsPayload("doc-1", "hello", 0.9)));
        var service = CreateService(handler);

        var results = await service.SearchAsync("hello");

        results.Should().ContainSingle();
        results[0].Key.Should().Be("doc-1");
    }

    [Fact]
    public async Task SearchAsync_populates_cache_used_on_unhealthy_fallback()
    {
        var callCount = 0;
        var handler = RespondWith(() =>
        {
            callCount++;
            return McpSuccess("search", SearchResultsPayload("doc-1", "content", 0.8));
        });
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);
        await service.SearchAsync("cached-query");

        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var results = await service.SearchAsync("cached-query");

        results.Should().ContainSingle();
        results[0].Key.Should().Be("doc-1");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_inner_throws_returns_cached_results_when_available()
    {
        var handler = RespondWith(() => McpSuccess("search", SearchResultsPayload("doc-1", "cached", 0.7)));
        var service = CreateService(handler);

        await service.SearchAsync("query");

        handler.SwitchToError("transient failure");
        var results = await service.SearchAsync("query");

        results.Should().ContainSingle();
        results[0].Key.Should().Be("doc-1");
    }

    [Fact]
    public async Task SearchAsync_inner_throws_no_cache_returns_empty()
    {
        var handler = RespondWith(() => McpError("search", "boom"));
        var service = CreateService(handler);

        var results = await service.SearchAsync("uncached-query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_unhealthy_provider_returns_cached_results_when_available()
    {
        var handler = RespondWith(() => McpSuccess("search", SearchResultsPayload("doc-1", "data", 0.6)));
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);
        await service.SearchAsync("query");

        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var results = await service.SearchAsync("query");

        results.Should().ContainSingle();
        results[0].Key.Should().Be("doc-1");
    }

    [Fact]
    public async Task SearchAsync_unhealthy_provider_no_cache_returns_empty()
    {
        var healthTracker = new Mock<IProviderHealthTracker>();
        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var service = CreateService(healthTracker: healthTracker.Object);

        var results = await service.SearchAsync("uncached-query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_cancellation_rethrows_OperationCanceledException()
    {
        var handler = RespondWith(() => throw new OperationCanceledException());
        var service = CreateService(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchAsync("query", ct: new CancellationToken(true)));
    }

    [Fact]
    public async Task SearchAsync_records_healthy_on_success()
    {
        var handler = RespondWith(McpSuccess("search", SearchResultsPayload("doc-1", "ok", 1.0)));
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);

        await service.SearchAsync("query");

        healthTracker.Verify(h => h.RecordProbeResult(
            ProviderNames.GBrain,
            It.Is<ProviderProbeResult>(r => r.IsHealthy)), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_records_unhealthy_on_failure()
    {
        var handler = RespondWith(() => McpError("search", "service down"));
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);

        await service.SearchAsync("query");

        healthTracker.Verify(h => h.RecordProbeResult(
            ProviderNames.GBrain,
            It.Is<ProviderProbeResult>(r => !r.IsHealthy)), Times.Once);
    }

    // ---------------------------------------------------------------
    // GetPageAsync
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPageAsync_throws_ArgumentException_on_null_or_whitespace_key(string? key)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetPageAsync(key!));
    }

    [Fact]
    public async Task GetPageAsync_healthy_provider_returns_page_from_inner_service()
    {
        var handler = RespondWith(McpSuccess("get_page", PagePayload("wiki/docs", "# Docs")));
        var service = CreateService(handler);

        var page = await service.GetPageAsync("wiki/docs");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/docs");
        page.Content.Should().Be("# Docs");
    }

    [Fact]
    public async Task GetPageAsync_populates_cache_used_on_unhealthy_fallback()
    {
        var callCount = 0;
        var handler = RespondWith(() =>
        {
            callCount++;
            return McpSuccess("get_page", PagePayload("wiki/cached", "content"));
        });
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);
        await service.GetPageAsync("wiki/cached");

        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var page = await service.GetPageAsync("wiki/cached");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/cached");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_inner_throws_returns_cached_page_when_available()
    {
        var handler = RespondWith(() => McpSuccess("get_page", PagePayload("wiki/saved", "cached content")));
        var service = CreateService(handler);

        await service.GetPageAsync("wiki/saved");

        handler.SwitchToError("transient");
        var page = await service.GetPageAsync("wiki/saved");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/saved");
    }

    [Fact]
    public async Task GetPageAsync_inner_throws_no_cache_returns_null()
    {
        var handler = RespondWith(() => McpError("get_page", "boom"));
        var service = CreateService(handler);

        var page = await service.GetPageAsync("wiki/missing");

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_unhealthy_provider_returns_cached_page_when_available()
    {
        var handler = RespondWith(() => McpSuccess("get_page", PagePayload("wiki/page", "data")));
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);
        await service.GetPageAsync("wiki/page");

        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var page = await service.GetPageAsync("wiki/page");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/page");
    }

    [Fact]
    public async Task GetPageAsync_unhealthy_provider_no_cache_returns_null()
    {
        var healthTracker = new Mock<IProviderHealthTracker>();
        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var service = CreateService(healthTracker: healthTracker.Object);

        var page = await service.GetPageAsync("wiki/missing");

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_records_healthy_on_success()
    {
        var handler = RespondWith(McpSuccess("get_page", PagePayload("wiki/x", "ok")));
        var healthTracker = CreateHealthyTracker();

        var service = CreateService(handler, healthTracker.Object);

        await service.GetPageAsync("wiki/x");

        healthTracker.Verify(h => h.RecordProbeResult(
            ProviderNames.GBrain,
            It.Is<ProviderProbeResult>(r => r.IsHealthy)), Times.Once);
    }

    // ---------------------------------------------------------------
    // PutPageAsync
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PutPageAsync_throws_ArgumentException_on_null_or_whitespace_key(string? key)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.PutPageAsync(key!, "content"));
    }

    [Fact]
    public async Task PutPageAsync_delegates_to_inner_service()
    {
        var handler = RespondWith(McpSuccess("put_page", new { }));
        var service = CreateService(handler);

        await service.PutPageAsync("wiki/new", "# New Page");

        handler.LastRequestBody.Should().NotBeNull();
        var doc = JsonDocument.Parse(handler.LastRequestBody!);
        doc.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("put_page");
    }

    [Fact]
    public async Task PutPageAsync_caches_written_page()
    {
        var handler = RespondWith(McpSuccess("put_page", new { }));
        var service = CreateService(handler);

        await service.PutPageAsync("wiki/cached", "# Cached");
        var page = await service.GetPageAsync("wiki/cached");

        page.Should().NotBeNull();
        page!.Key.Should().Be("wiki/cached");
        page.Content.Should().Be("# Cached");
    }

    [Fact]
    public async Task PutPageAsync_unhealthy_provider_skips_write()
    {
        var callCount = 0;
        var handler = RespondWith(() =>
        {
            callCount++;
            return McpSuccess("put_page", new { });
        });
        var healthTracker = new Mock<IProviderHealthTracker>();
        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var service = CreateService(handler, healthTracker.Object);

        await service.PutPageAsync("wiki/skip", "content");

        callCount.Should().Be(0);
    }

    [Fact]
    public async Task PutPageAsync_inner_throws_does_not_propagate()
    {
        var handler = RespondWith(() => McpError("put_page", "storage error"));
        var service = CreateService(handler);

        var act = () => service.PutPageAsync("wiki/fail", "content");

        await act.Should().NotThrowAsync();
    }

    // ---------------------------------------------------------------
    // DeletePageAsync
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeletePageAsync_throws_ArgumentException_on_null_or_whitespace_key(string? key)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeletePageAsync(key!));
    }

    [Fact]
    public async Task DeletePageAsync_delegates_to_inner_service()
    {
        var handler = RespondWith(McpSuccess("delete_page", new { }));
        var service = CreateService(handler);

        await service.DeletePageAsync("wiki/old");

        handler.LastRequestBody.Should().NotBeNull();
        var doc = JsonDocument.Parse(handler.LastRequestBody!);
        doc.RootElement.GetProperty("params").GetProperty("name").GetString().Should().Be("delete_page");
    }

    [Fact]
    public async Task DeletePageAsync_removes_page_from_cache()
    {
        var handler = RespondWith(McpSuccess("put_page", new { }));
        var service = CreateService(handler);
        await service.PutPageAsync("wiki/gone", "content");

        handler.SwitchToSuccess("delete_page", new { });
        await service.DeletePageAsync("wiki/gone");

        handler.SwitchToSuccess("get_page", PagePayload("wiki/gone", "re-fetched"));
        var page = await service.GetPageAsync("wiki/gone");
        page.Should().NotBeNull("GetPageAsync re-fetches from inner after cache was cleared by delete");
        page!.Key.Should().Be("wiki/gone");
    }

    [Fact]
    public async Task DeletePageAsync_unhealthy_provider_skips_delete_and_clears_cache()
    {
        var handler = RespondWith(McpSuccess("put_page", new { }));
        var service = CreateService(handler);
        await service.PutPageAsync("wiki/stale", "content");

        var healthTracker = new Mock<IProviderHealthTracker>();
        healthTracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(UnhealthyStatus);

        var deleteHandler = RespondWith(McpSuccess("delete_page", new { }));
        var deleteService = CreateService(deleteHandler, healthTracker.Object);
        await deleteService.DeletePageAsync("wiki/stale");
        var page = await deleteService.GetPageAsync("wiki/stale");

        page.Should().BeNull();
    }

    [Fact]
    public async Task DeletePageAsync_inner_throws_does_not_propagate()
    {
        var handler = RespondWith(() => McpError("delete_page", "not found"));
        var service = CreateService(handler);

        var act = () => service.DeletePageAsync("wiki/missing");

        await act.Should().NotThrowAsync();
    }

    // ---------------------------------------------------------------
    // Null health tracker (default path)
    // ---------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_without_health_tracker_records_healthy_on_success()
    {
        var handler = RespondWith(McpSuccess("search", SearchResultsPayload("d", "q", 1.0)));
        var service = CreateService(handler);

        var results = await service.SearchAsync("q");

        results.Should().ContainSingle();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static Mock<IProviderHealthTracker> CreateHealthyTracker()
    {
        var tracker = new Mock<IProviderHealthTracker>();
        tracker.Setup(h => h.GetStatus(ProviderNames.GBrain))
            .Returns(new ProviderHealthStatus
            {
                ProviderName = ProviderNames.GBrain,
                State = Abstractions.Enums.ProviderHealthState.Healthy,
            });
        return tracker;
    }

    private static readonly ProviderHealthStatus UnhealthyStatus = new()
    {
        ProviderName = ProviderNames.GBrain,
        State = Abstractions.Enums.ProviderHealthState.Unhealthy,
    };

    private static ConfigurableHttpMessageHandler RespondWith(Func<HttpResponseMessage> responseFactory)
        => new(responseFactory);

    private static ConfigurableHttpMessageHandler RespondWith(HttpResponseMessage response)
        => new(() => response);

    private static ResilientKnowledgeService CreateService(
        ConfigurableHttpMessageHandler? handler = null,
        IProviderHealthTracker? healthTracker = null)
    {
        handler ??= RespondWith(() => McpSuccess("search", new { results = Array.Empty<object>() }));
        var client = new GBrainMcpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<GBrainMcpClient>.Instance);

        var inner = new GBrainKnowledgeService(client, NullLogger<GBrainKnowledgeService>.Instance);

        return new ResilientKnowledgeService(
            inner,
            NullLogger<ResilientKnowledgeService>.Instance,
            healthTracker);
    }

    private static HttpResponseMessage McpSuccess(string toolName, object resultPayload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(resultPayload) } } },
            }),
        };

    private static HttpResponseMessage McpError(string toolName, string errorMessage)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    isError = true,
                    content = new[] { new { type = "text", text = errorMessage } },
                },
            }),
        };

    private static object SearchResultsPayload(string slug, string content, double score)
        => new { results = new[] { new { slug, compiled_truth = content, score } } };

    private static object PagePayload(string slug, string content)
        => new { slug, compiled_truth = content, updated_at = DateTimeOffset.UtcNow };

    private sealed class ConfigurableHttpMessageHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _responseFactory;

        public ConfigurableHttpMessageHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public string? LastRequestBody { get; private set; }

        public void SwitchToError(string errorMessage)
        {
            _responseFactory = () => McpError("error", errorMessage);
        }

        public void SwitchToSuccess(string toolName, object resultPayload)
        {
            _responseFactory = () => McpSuccess(toolName, resultPayload);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return _responseFactory();
        }
    }
}
