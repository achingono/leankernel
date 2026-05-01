using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Archivist;

public class ContextGatekeeperFullTests
{
    [Theory]
    [InlineData("who is Alice?", WikiDimension.Who)]
    [InlineData("what happened?", WikiDimension.What)]
    [InlineData("where is it?", WikiDimension.Where)]
    [InlineData("when is the meeting?", WikiDimension.When)]
    [InlineData("why did it fail?", WikiDimension.Why)]
    [InlineData("how do I fix it?", WikiDimension.How)]
    public void ClassifyDimensions_DetectsKeywords(string query, WikiDimension expected)
    {
        var dims = ContextGatekeeper.ClassifyDimensions(query);
        Assert.Contains(expected, dims);
    }

    [Fact]
    public void ClassifyDimensions_PersonKeyword_MapsToWho()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("tell me about that person");
        Assert.Contains(WikiDimension.Who, dims);
    }

    [Fact]
    public void ClassifyDimensions_ContactKeyword_MapsToWho()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("find this contact");
        Assert.Contains(WikiDimension.Who, dims);
    }

    [Fact]
    public void ClassifyDimensions_LocationKeyword_MapsToWhere()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("what location?");
        Assert.Contains(WikiDimension.Where, dims);
    }

    [Fact]
    public void ClassifyDimensions_ScheduleKeyword_MapsToWhen()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("check the schedule");
        Assert.Contains(WikiDimension.When, dims);
    }

    [Fact]
    public void ClassifyDimensions_ProcessKeyword_MapsToHow()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("explain the process");
        Assert.Contains(WikiDimension.How, dims);
    }

    [Fact]
    public void ClassifyDimensions_NoDimensions_DefaultsToWhoWhat()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("hello");
        Assert.Contains(WikiDimension.Who, dims);
        Assert.Contains(WikiDimension.What, dims);
        Assert.Equal(2, dims.Count);
    }

    [Fact]
    public void ClassifyDimensions_MultipleDimensions_Detected()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("who did what and when?");
        Assert.Contains(WikiDimension.Who, dims);
        Assert.Contains(WikiDimension.What, dims);
        Assert.Contains(WikiDimension.When, dims);
    }

    [Fact]
    public void ClassifyDimensions_CaseInsensitive()
    {
        var dims = ContextGatekeeper.ClassifyDimensions("WHO is this PERSON?");
        Assert.Contains(WikiDimension.Who, dims);
    }

    [Fact]
    public async Task GateContextAsync_ReturnsContext()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage
        {
            Id = "m1", ChannelId = "test", SenderId = "u1", Content = "hello"
        };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.NotNull(ctx);
        Assert.NotEmpty(ctx.SystemPrompt);
        Assert.Empty(ctx.WikiLeanKernels);
        Assert.Empty(ctx.RetrievedLeanKernels);
    }

    [Fact]
    public async Task GateContextAsync_WithHistory_AppliesTieredAging()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        // 12 turns of history
        var history = Enumerable.Range(0, 12).Select(i => new ConversationTurn
        {
            Role = i % 2 == 0 ? "user" : "assistant",
            Content = new string('x', 600),
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-i)
        }).ToList();

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(history));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "hi" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        // Oldest turns should be compacted
        Assert.True(ctx.History.Any(t => t.IsCompacted));
        // Recent turns should be full (600 chars)
        Assert.True(ctx.History.Last().Content.Length == 600);
    }

    [Fact]
    public async Task GateContextAsync_WithWikiEntries_RanksAndIncludes()
    {
        var wikiEntry = new WikiEntry
        {
            Id = "who-alice",
            Dimension = WikiDimension.Who,
            Subject = "Alice",
            Facts = [new WikiFact { Claim = "Alice is a dev", Confidence = 0.9, EstimatedTokens = 5 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 10
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([wikiEntry]));
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig { Context = new ContextConfig { MinRelevanceThreshold = 0.0 } });
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "who is Alice?" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.NotEmpty(ctx.WikiLeanKernels);
        Assert.Contains("Alice", ctx.WikiLeanKernels[0].Content);
    }
}
