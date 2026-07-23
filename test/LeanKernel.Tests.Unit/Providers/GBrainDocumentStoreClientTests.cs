using System.Text.Json;

using FluentAssertions;

using LeanKernel;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Memory;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public sealed class GBrainDocumentStoreClientTests
{
    [Fact]
    public async Task ExistsAsync_NotFoundFromGBrain_ReturnsFalse()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("get_page", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("missing", -32601));
        var sut = CreateSut(mcp.Object);

        var exists = await sut.ExistsAsync(CreateScope(), "fp-1");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_MapsAndFiltersResultsByChannelIds()
    {
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/channel/{{channelA}}/{{userId}}/fp-a",
                  "title": "alpha.txt",
                  "compiled_truth": "{{new string('x', 230)}}",
                  "score": 0.9
                },
                {
                  "slug": "documents/{{tenantId}}/channel/{{channelB}}/{{userId}}/fp-b",
                  "title": "beta.txt",
                  "content": "beta",
                  "score": 0.1
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channelA, DocumentAvailabilityScope.Channel);
        var hits = await sut.SearchAsync(scope, "alpha", [channelA], 20);

        hits.Should().HaveCount(1);
        hits[0].FileName.Should().Be("alpha.txt");
        hits[0].Fingerprint.Should().Contain(channelA.ToString());
        hits[0].Excerpt.Should().EndWith("...");
        hits[0].Excerpt.Length.Should().Be(203);
    }

    [Fact]
    public async Task SearchAsync_NonStringContent_DoesNotThrowAndUsesStringRepresentation()
    {
        var channel = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/channel/{{channel}}/{{userId}}/fp-a",
                  "title": "alpha.txt",
                  "compiled_truth": 42,
                  "score": 0.5
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.Channel);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 5);

        hits.Should().HaveCount(1);
        hits[0].Excerpt.Should().Be("42");
    }

    [Fact]
    public async Task ListAsync_ParsesArrayPayloadAndFiltersCatalogEntries()
    {
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{tenantId}}/channel/{{channelA}}/{{userA}}/fp-a",
                "content": "alpha"
              },
              {
                "slug": "documents/{{tenantId}}/channel/{{channelB}}/{{userB}}/fp-b",
                "compiled_truth": "beta"
              }
            ]
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userA, Guid.NewGuid(), channelA, DocumentAvailabilityScope.Channel);
        var entries = await sut.ListAsync(scope, [channelA], 10);

        entries.Should().HaveCount(1);
        entries[0].ChannelId.Should().Be(channelA);
        entries[0].UserId.Should().Be(userA);
        entries[0].ExtractedText.Should().Be("alpha");
    }

    [Fact]
    public async Task ListAsync_GBrainError_ReturnsEmpty()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("boom", 500));
        var sut = CreateSut(mcp.Object);

        var result = await sut.ListAsync(CreateScope(), channelIds: null, limit: 10);

        result.Should().BeEmpty();
    }

    private static GBrainDocumentStoreClient CreateSut(IGBrainMcpClient mcp)
        => new(
            mcp,
            Options.Create(new GBrainSettings()),
            NullLogger<GBrainDocumentStoreClient>.Instance);

    private static DocumentScopeContext CreateScope()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DocumentAvailabilityScope.Channel);
}
