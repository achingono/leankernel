using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.TurnRuntime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class ScopedRetrievalStageTests
{
    private static IOptions<TurnPipelineSettings> CreateSettings(int maxRetrievalCandidates = 10)
        => Options.Create(new TurnPipelineSettings { MaxRetrievalCandidates = maxRetrievalCandidates });

    private static TurnContext CreateContext(string userMessage = "What is my name?", IPermit? permit = null)
    {
        return new TurnContext
        {
            Permit = permit ?? CreatePermit(),
            UserMessage = userMessage,
            ConversationId = "conv-1",
        };
    }

    private static IPermit CreatePermit(
        Guid? tenantId = null,
        Guid? userId = null,
        Guid? personId = null,
        Guid? channelId = null)
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.UserId).Returns(userId ?? Guid.NewGuid());
        mock.Setup(p => p.PersonId).Returns(personId ?? userId ?? Guid.NewGuid());
        mock.Setup(p => p.TenantId).Returns(tenantId ?? Guid.NewGuid());
        mock.Setup(p => p.ChannelId).Returns(channelId ?? Guid.NewGuid());
        mock.Setup(p => p.HostName).Returns("localhost");
        mock.Setup(p => p.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteAsync_EmptyUserMessage_SkipsRetrieval()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var ctx = CreateContext(userMessage: string.Empty);
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates.Should().BeEmpty();
        memoryClient.Verify(
            m => m.SearchMemoriesAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MemoriesReturned_AddsCandidates()
    {
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryItem>
            {
                new() { Key = "facts/what/name", Text = "User is Alice", Score = 0.9 },
                new() { Key = "facts/where/city", Text = "User lives in Portland", Score = 0.7 },
            });

        var ctx = CreateContext("What is my name?");
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates.Should().HaveCount(2);
        ctx.Candidates[0].Source.Should().Be("memory");
        ctx.Candidates[0].Content.Should().Be("User is Alice");
        ctx.Candidates[0].Score.Should().Be(0.9);
        ctx.Candidates[1].Content.Should().Be("User lives in Portland");
    }

    [Fact]
    public async Task ExecuteAsync_MemoryScope_UsesPermitPartitioning()
    {
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        MemoryScope? capturedScope = null;
        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<MemoryScope, string, int, CancellationToken>((scope, _, _, _) => capturedScope = scope)
            .ReturnsAsync(new List<MemoryItem>());

        var ctx = CreateContext(permit: CreatePermit(tenantId, Guid.NewGuid(), personId, channelId));
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        capturedScope.Should().NotBeNull();
        capturedScope!.TenantId.Should().Be(tenantId);
        capturedScope.PersonId.Should().Be(personId);
        capturedScope.ChannelId.Should().Be(channelId);
    }

    [Fact]
    public async Task ExecuteAsync_NoMemories_DoesNotAddCandidates()
    {
        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryItem>());

        var ctx = CreateContext();
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MemorySearchFails_DegradesGracefully()
    {
        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Memory service unavailable"));

        var ctx = CreateContext();
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        var act = () => stage.ExecuteAsync(ctx);

        await act.Should().NotThrowAsync();
        ctx.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CandidatesHaveMetadata_ContainsScopeKeys()
    {
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryItem>
            {
                new() { Key = "facts/what/name", Text = "Alice", Score = 0.8 },
            });

        var ctx = CreateContext(permit: CreatePermit(tenantId, Guid.NewGuid(), personId, channelId));
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates.Should().HaveCount(1);
        var candidate = ctx.Candidates[0];
        candidate.Metadata["tenant_id"].Should().Be(tenantId.ToString());
        candidate.Metadata["person_id"].Should().Be(personId.ToString());
        candidate.Metadata["channel_id"].Should().Be(channelId.ToString());
        candidate.Metadata["memory_key"].Should().Be("facts/what/name");
    }

    [Fact]
    public async Task ExecuteAsync_EstimatedTokens_UsesCharsPerTokenHeuristic()
    {
        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryItem>
            {
                new() { Key = "k", Text = new string('a', 100), Score = 0.5 },
            });

        var ctx = CreateContext();
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates[0].EstimatedTokens.Should().Be(25);
    }

    [Fact]
    public async Task ExecuteAsync_MemoryTextEmpty_UsesKeyAsContent()
    {
        var memoryClient = new Mock<IMemoryClient>();
        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryItem>
            {
                new() { Key = "facts/what/only-key", Text = string.Empty, Score = 0.6 },
            });

        var ctx = CreateContext();
        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(ctx);

        ctx.Candidates.Should().HaveCount(1);
        ctx.Candidates[0].Content.Should().Be("facts/what/only-key");
    }

    [Fact]
    public void Name_ReturnsScopedRetrieval()
    {
        var stage = new ScopedRetrievalStage(
            Mock.Of<IMemoryClient>(),
            CreateSettings(),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        stage.Name.Should().Be("ScopedRetrieval");
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredMaxRetrievalCandidates()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var configuredMax = 7;
        var requestedMax = -1;

        memoryClient
            .Setup(m => m.SearchMemoriesAsync(
                It.IsAny<MemoryScope>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<MemoryScope, string, int, CancellationToken>((_, _, max, _) => requestedMax = max)
            .ReturnsAsync(new List<MemoryItem>());

        var stage = new ScopedRetrievalStage(
            memoryClient.Object,
            CreateSettings(configuredMax),
            Mock.Of<ILogger<ScopedRetrievalStage>>());

        await stage.ExecuteAsync(CreateContext());

        requestedMax.Should().Be(configuredMax);
    }
}