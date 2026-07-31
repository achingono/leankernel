using System.Text.Json;

using FluentAssertions;

using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Configuration;
using LeanKernel.Services.Common.Interfaces;
using LeanKernel.Services.Common.Memory;

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
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
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
    public async Task SearchAsync_GBrainError_ReturnsEmpty()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("boom", 500));
        var sut = CreateSut(mcp.Object);

        var result = await sut.SearchAsync(CreateScope(), "alpha", channelIds: null, maxResults: 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_GBrainError_ReturnsEmpty()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("boom", 500));
        var sut = CreateSut(mcp.Object);

        var result = await sut.ListAsync(CreateScope(), channelIds: null, limit: 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_UserScopedCanonicalHits_SurfaceRegardlessOfChannel()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-a",
                  "title": "canonical.txt",
                  "compiled_truth": "alpha",
                  "score": 0.8
                },
                {
                  "slug": "memory/{{tenantId}}/00000000-0000-0000-0000-000000000000/{{channel}}/facts/what/key",
                  "title": "not-a-document",
                  "compiled_truth": "beta",
                  "score": 0.9
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 10);

        hits.Should().HaveCount(1);
        hits[0].FileName.Should().Be("canonical.txt");
        hits[0].Fingerprint.Should().Contain("user/00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesByFingerprint_KeepsHighestScore()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/channel/{{channel}}/{{userId}}/fp-x",
                  "title": "channel-copy.txt",
                  "compiled_truth": "alpha",
                  "score": 0.4
                },
                {
                  "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-x",
                  "title": "canonical-copy.txt",
                  "compiled_truth": "alpha",
                  "score": 0.9
                },
                {
                  "slug": "documents/{{tenantId}}/channel/{{channel}}/{{userId}}/fp-y",
                  "title": "distinct.txt",
                  "compiled_truth": "gamma",
                  "score": 0.2
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 10);

        hits.Should().HaveCount(2);
        hits.Should().OnlyContain(h => h.Fingerprint.EndsWith("fp-x") || h.Fingerprint.EndsWith("fp-y"));
        var merged = hits.Single(h => h.Fingerprint.EndsWith("fp-x"));
        merged.FileName.Should().Be("canonical-copy.txt");
        merged.Score.Should().Be(0.9);
    }

    [Fact]
    public async Task SearchAsync_DedupeTieBreak_UsesLexicalSlugOrder()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/user/{{channel}}/{{userId}}/fp-z",
                  "title": "z-copy.txt",
                  "compiled_truth": "alpha",
                  "score": 0.5
                },
                {
                  "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-z",
                  "title": "canonical-copy.txt",
                  "compiled_truth": "alpha",
                  "score": 0.5
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 10);

        hits.Should().ContainSingle();
        hits[0].Fingerprint.Should().Contain("user/00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public async Task SearchAsync_UserScopedHitOfAnotherUser_IsDropped()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{otherUser}}/fp-a",
                  "title": "other-user.txt",
                  "compiled_truth": "alpha",
                  "score": 0.9
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 10);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_TenantMismatch_IsDropped()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{otherTenant}}/channel/{{channel}}/{{userId}}/fp-a",
                  "title": "other-tenant.txt",
                  "compiled_truth": "alpha",
                  "score": 0.9
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.Channel);
        var hits = await sut.SearchAsync(scope, "alpha", [channel], 10);

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_UsesListPages_DeduplicatesAndOrdersByUpdatedAt()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-old",
                "updated_at": "2026-01-01T00:00:00Z"
              },
              {
                "slug": "documents/{{tenantId}}/user/{{channel}}/{{userId}}/fp-new",
                "updated_at": "2026-06-01T00:00:00Z"
              },
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-new",
                "updated_at": "2026-03-01T00:00:00Z"
              },
              {
                "slug": "memory/{{tenantId}}/00000000-0000-0000-0000-000000000000/{{channel}}/facts/what/key",
                "updated_at": "2026-06-02T00:00:00Z"
              }
            ]
            """).RootElement.Clone();

        object? capturedArgs = null;
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, CancellationToken>((_, args, _) => capturedArgs = args)
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var entries = await sut.ListAsync(scope, [channel], 10);

        capturedArgs.Should().NotBeNull();
        var sort = capturedArgs!.GetType().GetProperty("sort")!.GetValue(capturedArgs);
        sort.Should().Be("updated_desc");
        var type = capturedArgs.GetType().GetProperty("type")!.GetValue(capturedArgs);
        type.Should().Be("document");
        var limit = capturedArgs.GetType().GetProperty("limit")!.GetValue(capturedArgs);
        limit.Should().Be(100);

        entries.Should().HaveCount(2);
        entries[0].Fingerprint.Should().EndWith("fp-new");
        entries[0].AvailabilityScope.Should().Be(DocumentAvailabilityScope.User);
        entries[0].IngestedAt.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        entries[1].Fingerprint.Should().EndWith("fp-old");
        entries[1].ChannelId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ListAsync_ChannelScopedEntries_FilteredByReadableChannels()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{tenantId}}/channel/{{channelA}}/{{userId}}/fp-a",
                "updated_at": "2026-01-01T00:00:00Z"
              },
              {
                "slug": "documents/{{tenantId}}/channel/{{channelB}}/{{userId}}/fp-b",
                "updated_at": "2026-01-02T00:00:00Z"
              }
            ]
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channelA, DocumentAvailabilityScope.Channel);
        var entries = await sut.ListAsync(scope, [channelA], 10);

        entries.Should().ContainSingle();
        entries[0].Fingerprint.Should().EndWith("fp-a");
        entries[0].ChannelId.Should().Be(channelA);
    }

    [Fact]
    public async Task ListAsync_PaginatesViaOffset_WhenLimitExceedsRemotePageLimit()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var callCount = 0;
        var capturedOffsets = new List<int>();

        // Page 1: 100 items (full page) to force pagination - dates in 2025
        var page1Items = new System.Text.StringBuilder("[");
        for (int i = 1; i <= 100; i++)
        {
            var month = ((i - 1) / 28) + 1; // 1-4 (Jan-Apr)
            var day = ((i - 1) % 28) + 1; // 1-28
            if (i > 1)
            {
                page1Items.Append(",");
            }

            page1Items.Append($$"""
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-{{i}}",
                "updated_at": "2025-{{month:D2}}-{{day:D2}}T00:00:00Z"
              }
            """);
        }

        page1Items.Append("]");
        var page1Payload = JsonDocument.Parse(page1Items.ToString()).RootElement.Clone();

        // Page 2: 3 items (partial page) - newer dates in 2026
        var page2Payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-101",
                "updated_at": "2026-01-01T00:00:00Z"
              },
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-102",
                "updated_at": "2026-01-02T00:00:00Z"
              },
              {
                "slug": "documents/{{tenantId}}/user/00000000-0000-0000-0000-000000000000/{{userId}}/fp-103",
                "updated_at": "2026-01-03T00:00:00Z"
              }
            ]
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, CancellationToken>((_, args, _) =>
            {
                callCount++;
                var offsetProp = args!.GetType().GetProperty("offset");
                if (offsetProp != null)
                {
                    capturedOffsets.Add((int)offsetProp.GetValue(args)!);
                }
            })
            .ReturnsAsync(() => callCount == 1 ? page1Payload : page2Payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.User);
        var entries = await sut.ListAsync(scope, [channel], 200);

        callCount.Should().Be(2);
        capturedOffsets.Should().BeEquivalentTo(new[] { 0, 100 });
        entries.Should().HaveCount(103);
        entries[0].Fingerprint.Should().EndWith("fp-103");
        entries[102].Fingerprint.Should().EndWith("fp-1");
    }

    [Fact]
    public async Task SearchAsync_NullResult_ReturnsEmpty()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonElement?)null);
        var sut = CreateSut(mcp.Object);

        var result = await sut.SearchAsync(CreateScope(), "alpha", channelIds: null, maxResults: 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_NullResult_ReturnsEmpty()
    {
        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonElement?)null);
        var sut = CreateSut(mcp.Object);

        var result = await sut.ListAsync(CreateScope(), channelIds: null, limit: 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IsReadable_TenantMismatch_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{otherTenant}}/tenant/{{channel}}/{{userId}}/fp-1",
                "content": "test"
              }
            ]
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.Tenant);
        var entries = await sut.ListAsync(scope, [channel], 10);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task IsReadable_UnknownScope_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            [
              {
                "slug": "documents/{{tenantId}}/unknownscope/{{channel}}/{{userId}}/fp-1",
                "content": "test"
              }
            ]
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("list_pages", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.Channel);
        var entries = await sut.ListAsync(scope, [channel], 10);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task MapToSearchHit_SupportsNumericAndBooleanContent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channel = Guid.NewGuid();

        var payload = JsonDocument.Parse(
            $$"""
            {
              "results": [
                {
                  "slug": "documents/{{tenantId}}/channel/{{channel}}/{{userId}}/fp-num",
                  "title": "num.txt",
                  "content": 123,
                  "score": 0.5
                },
                {
                  "slug": "documents/{{tenantId}}/channel/{{channel}}/{{userId}}/fp-bool",
                  "title": "bool.txt",
                  "compiled_truth": true,
                  "score": 0.4
                }
              ]
            }
            """).RootElement.Clone();

        var mcp = new Mock<IGBrainMcpClient>();
        mcp.Setup(c => c.CallToolAsync("search", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payload);
        var sut = CreateSut(mcp.Object);

        var scope = new DocumentScopeContext(tenantId, userId, Guid.NewGuid(), channel, DocumentAvailabilityScope.Channel);
        var hits = await sut.SearchAsync(scope, "test", [channel], 10);

        hits.Should().HaveCount(2);
        hits[0].Excerpt.Should().Be("123");
        hits[1].Excerpt.Should().Be("True");
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