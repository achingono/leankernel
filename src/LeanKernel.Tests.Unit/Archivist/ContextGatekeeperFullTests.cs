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

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0
            }
        });
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

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0
            }
        });
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "hi" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        // Oldest turns should be compacted
        Assert.Contains(ctx.History, t => t.IsCompacted);
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

    [Fact]
    public async Task GateContextAsync_WikiEntryWithLexicalOverlap_PassesDefaultThreshold()
    {
        var wikiEntry = new WikiEntry
        {
            Id = "who-user-profile",
            Dimension = WikiDimension.Who,
            Subject = "User",
            Facts = [new WikiFact { Claim = "User name is Ada Lovelace", Confidence = 0.9, EstimatedTokens = 6 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 0
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([wikiEntry]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "what is my name" };
        var budget = ContextBudget.FromModelWindow(128_000);

        await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        await wiki.Received().QueryAsync(
            Arg.Is<WikiQuery>(q => q.TextQuery == "what is my name"
                && q.Dimensions.Contains(WikiDimension.Who)
                && q.Dimensions.Contains(WikiDimension.What)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateContextAsync_WithAgentTags_PassesTagsToKnowledgeSearch()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "search docs" };
        var budget = ContextBudget.FromModelWindow(128_000);
        var tags = new List<string> { "technical", "wiki" };

        await gatekeeper.GateContextAsync(msg, budget, "s1", tags, CancellationToken.None);

        await knowledgeSearch.Received().SearchAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(t => t.Contains("technical") && t.Contains("wiki")),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateContextAsync_VectorResults_NotExcludedByThreshold()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        // Return a vector result with high semantic similarity
        var vectorResult = new RelevanceScore
        {
            EntryId = "doc-1",
            Content = "Technical document content",
            EstimatedTokens = 20,
            SemanticSimilarity = 0.85,
            Score = 0.85,
            SourceType = RelevanceSourceType.Vector
        };
        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore> { vectorResult }));

        // Default threshold is 0.65 — vector result with 0.85 semantic should pass
        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "technical question" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.NotEmpty(ctx.RetrievedLeanKernels);
        Assert.Equal("doc-1", ctx.RetrievedLeanKernels[0].EntryId);
    }

    [Fact]
    public async Task GateContextAsync_EntityMention_AddsWhoDimensionForSchedulingQuery()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig());
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I was thinking of scheduling a 1-1 with John" };
        var budget = ContextBudget.FromModelWindow(128_000);

        await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        await wiki.Received().QueryAsync(
            Arg.Is<WikiQuery>(q => q.Dimensions.Contains(WikiDimension.Who)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateContextAsync_PinnedEntityCandidate_BypassesDefaultThreshold()
    {
        var johnEntry = new WikiEntry
        {
            Id = "who-john-smith",
            Dimension = WikiDimension.Who,
            Subject = "John Smith",
            Aliases = ["John"],
            Facts = [new WikiFact { Claim = "not specified", Confidence = 0.9, EstimatedTokens = 3 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 0
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([johnEntry]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.95
            }
        });
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I was thinking of scheduling a 1-1 with John" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.Contains(ctx.WikiLeanKernels, k => k.EntryId == "who-john-smith");
    }

    [Fact]
    public async Task GateContextAsync_AmbiguousEntityName_AddsDisambiguationHints()
    {
        var johnSmith = new WikiEntry
        {
            Id = "who-john-smith",
            Dimension = WikiDimension.Who,
            Subject = "John Smith",
            Aliases = ["John"],
            Facts = [new WikiFact { Claim = "John Smith is CTO at Teachers", Confidence = 0.9, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 5
        };
        var johnWoo = new WikiEntry
        {
            Id = "who-john-woo",
            Dimension = WikiDimension.Who,
            Subject = "John Woo",
            Aliases = ["John"],
            Facts = [new WikiFact { Claim = "John Woo is CIO", Confidence = 0.8, EstimatedTokens = 6 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 3
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([johnSmith, johnWoo]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "Should I schedule a meeting with John?" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains("plausible references", ctx.DisambiguationHints[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GateContextAsync_UnclearLowConfidenceQuery_RunsFallbackDiscoveryAcrossWikiAndDocuments()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var gatekeeper = new ContextGatekeeper(
            wiki,
            sessions,
            knowledgeSearch,
            Options.Create(new LeanKernelConfig()),
            NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "It's him" };
        var budget = ContextBudget.FromModelWindow(128_000);

        await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        await wiki.Received(2).QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>());
        await knowledgeSearch.Received(2).SearchAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateContextAsync_AmbiguousNameWithClearHighConfidenceWinner_SkipsDisambiguation()
    {
        var strong = new WikiEntry
        {
            Id = "who-john-smith",
            Dimension = WikiDimension.Who,
            Subject = "John Smith",
            Aliases = ["John"],
            Facts = [new WikiFact { Claim = "John Smith is CTO and owns platform strategy leadership roadmap", Confidence = 0.95, EstimatedTokens = 10 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 25
        };
        var weak = new WikiEntry
        {
            Id = "who-john-archived",
            Dimension = WikiDimension.Who,
            Subject = "John Archived",
            Aliases = ["John"],
            Facts = [new WikiFact { Claim = "John", Confidence = 0.7, EstimatedTokens = 3 }],
            LastAccessed = DateTimeOffset.UtcNow.AddDays(-120),
            AccessCount = 0
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([strong, weak]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0,
                AmbiguityLowConfidenceThreshold = 0.0,
                AmbiguityConfidenceGapThreshold = 0.0
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "Tell me about John leadership strategy" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.Empty(ctx.DisambiguationHints);
    }
}
