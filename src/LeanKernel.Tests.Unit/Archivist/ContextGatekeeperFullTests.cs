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

    [Fact]
    public async Task GateContextAsync_RelationshipTokenCollision_AddsDisambiguationHint()
    {
        var family = new WikiEntry
        {
            Id = "who-family",
            Dimension = WikiDimension.Who,
            Subject = "Family",
            Facts = [new WikiFact { Claim = "Mother remembrance guidance lives in family roster.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 3
        };
        var mother = new WikiEntry
        {
            Id = "who-mary",
            Dimension = WikiDimension.Who,
            Subject = "Mary",
            Facts = [new WikiFact { Claim = "Mary is the user's mother.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 2
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([family, mother]));

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
            Options.Create(new LeanKernelConfig { Context = new ContextConfig { MinRelevanceThreshold = 0.0 } }),
            NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I'm thinking of my mother today" };
        var ctx = await gatekeeper.GateContextAsync(msg, ContextBudget.FromModelWindow(128_000), "s1", CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains("mother", ctx.DisambiguationHints[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GateContextAsync_PronounSingleClearAntecedent_SkipsDisambiguation()
    {
        var john = new WikiEntry
        {
            Id = "who-john-smith",
            Dimension = WikiDimension.Who,
            Subject = "John Smith",
            Facts = [new WikiFact { Claim = "John Smith is CTO at Teachers.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 12
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([john]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "I met John Smith today", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
            }));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0,
                AmbiguityLowConfidenceThreshold = 0.20
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "Should I meet him?" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.Empty(ctx.DisambiguationHints);
        Assert.Contains(ctx.WikiLeanKernels, k => k.EntryId == "who-john-smith");
    }

    [Fact]
    public async Task GateContextAsync_PronounRelationshipMismatch_PromptsClarification()
    {
        var mother = new WikiEntry
        {
            Id = "who-mary",
            Dimension = WikiDimension.Who,
            Subject = "Mary",
            Facts = [new WikiFact { Claim = "Mary is the user's mother.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 5
        };
        var father = new WikiEntry
        {
            Id = "who-peter",
            Dimension = WikiDimension.Who,
            Subject = "Peter",
            Facts = [new WikiFact { Claim = "Peter is the user's father.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 5
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([mother, father]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "I spoke with my father yesterday", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2) }
            }));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0,
                AmbiguityLowConfidenceThreshold = 1.01
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I'm thinking of my mother today, what about him?" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains(ctx.DisambiguationHints, hint => hint.Contains("mother", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GateContextAsync_PersonOrganizationTokenCollision_PromptsClarification()
    {
        var person = new WikiEntry
        {
            Id = "who-jordan-smith",
            Dimension = WikiDimension.Who,
            Subject = "Jordan Smith",
            Facts = [new WikiFact { Claim = "Jordan is a senior architect.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 8
        };
        var organization = new WikiEntry
        {
            Id = "where-jordan-program",
            Dimension = WikiDimension.Where,
            Subject = "Jordan Program",
            Facts = [new WikiFact { Claim = "Jordan program manages platform operations.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 8
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([person, organization]));

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
                AmbiguityLowConfidenceThreshold = 1.01
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I met Jordan and work with Jordan" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains(ctx.DisambiguationHints, hint => hint.Contains("jordan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GateContextAsync_WikiDocumentCollision_AddsCrossSourceDisambiguationReason()
    {
        var wikiEntry = new WikiEntry
        {
            Id = "who-phoenix-leadership",
            Dimension = WikiDimension.Who,
            Subject = "Phoenix",
            Facts = [new WikiFact { Claim = "Phoenix leads a platform initiative.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 10
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([wikiEntry]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var vector = new RelevanceScore
        {
            EntryId = "doc-phoenix-strategy",
            Content = "Phoenix strategy brief from documents.",
            EstimatedTokens = 20,
            SemanticSimilarity = 0.84,
            Score = 0.84,
            SourceType = RelevanceSourceType.Vector,
            KnowledgeSource = KnowledgeSourceType.Document
        };
        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore> { vector }));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0,
                AmbiguityLowConfidenceThreshold = 0.20,
                AmbiguityConfidenceGapThreshold = 0.15
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "Tell me about Phoenix strategy" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains("cross-source disagreement", ctx.DisambiguationHints[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GateContextAsync_PluralRelationshipAmbiguity_PromptsClarification()
    {
        var motherParents = new WikiEntry
        {
            Id = "who-mother-remembrance",
            Dimension = WikiDimension.Who,
            Subject = "Mother Remembrance",
            Facts = [new WikiFact { Claim = "Parents remembrance includes mother milestones.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 6
        };
        var fatherParents = new WikiEntry
        {
            Id = "who-father-remembrance",
            Dimension = WikiDimension.Who,
            Subject = "Father Remembrance",
            Facts = [new WikiFact { Claim = "Parents remembrance includes father milestones.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 6
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([motherParents, fatherParents]));

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
            Options.Create(new LeanKernelConfig { Context = new ContextConfig { MinRelevanceThreshold = 0.0 } }),
            NullLogger<ContextGatekeeper>.Instance);

        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "I'm thinking about my parents today" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains("parents", ctx.DisambiguationHints[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GateContextAsync_EllipticalFollowUpWithMultipleAntecedents_PromptsClarification()
    {
        var family = new WikiEntry
        {
            Id = "who-family",
            Dimension = WikiDimension.Who,
            Subject = "Family",
            Facts = [new WikiFact { Claim = "Mother and brother remembrance guidance is documented.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 4
        };
        var sibling = new WikiEntry
        {
            Id = "who-brother-profile",
            Dimension = WikiDimension.Who,
            Subject = "Brother Profile",
            Facts = [new WikiFact { Claim = "Mother and brother milestones are tracked here.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 4
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([family, sibling]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "I'm thinking of my mother today", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2) },
                new() { Role = "user", Content = "And my brother too!", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
            }));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var gatekeeper = new ContextGatekeeper(
            wiki,
            sessions,
            knowledgeSearch,
            Options.Create(new LeanKernelConfig { Context = new ContextConfig { MinRelevanceThreshold = 0.0 } }),
            NullLogger<ContextGatekeeper>.Instance);

        var ctx = await gatekeeper.GateContextAsync(
            new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "What do you know about her?" },
            ContextBudget.FromModelWindow(128_000),
            "s1",
            CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains(ctx.DisambiguationHints, hint => hint.Contains("mother", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("brother", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GateContextAsync_LowConfidencePronounSingleMatch_AddsDisambiguationHint()
    {
        var family = new WikiEntry
        {
            Id = "who-family",
            Dimension = WikiDimension.Who,
            Subject = "Family",
            Facts = [new WikiFact { Claim = "Mother remembrance guidance is documented.", Confidence = 0.95, EstimatedTokens = 8 }],
            LastAccessed = DateTimeOffset.UtcNow,
            AccessCount = 1
        };

        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([family]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "I'm thinking of my mother today", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
            }));

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RelevanceScore>()));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0,
                AmbiguityLowConfidenceThreshold = 1.01,
                AmbiguityConfidenceGapThreshold = 0.15
            }
        });

        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);
        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "What do you know about her?" };
        var budget = ContextBudget.FromModelWindow(128_000);

        var ctx = await gatekeeper.GateContextAsync(msg, budget, "s1", CancellationToken.None);

        Assert.NotEmpty(ctx.DisambiguationHints);
        Assert.Contains("confirm before asserting identity", ctx.DisambiguationHints[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GateContextAsync_FallbackMergeWithDuplicateVectorIds_DedupesWithoutThrowing()
    {
        var wiki = Substitute.For<IWikiStore>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>()));

        var duplicateVectorA = new RelevanceScore
        {
            EntryId = "who-karen-leung",
            Content = "Karen Leung profile from documents.",
            EstimatedTokens = 20,
            SemanticSimilarity = 0.40,
            Score = 0.40,
            SourceType = RelevanceSourceType.Vector
        };
        var duplicateVectorB = new RelevanceScore
        {
            EntryId = "who-karen-leung",
            Content = "Karen Leung profile from documents (refined).",
            EstimatedTokens = 18,
            SemanticSimilarity = 0.46,
            Score = 0.46,
            SourceType = RelevanceSourceType.Vector
        };
        var duplicateVectorDifferentSource = new RelevanceScore
        {
            EntryId = "who-karen-leung",
            Content = "Karen Leung wiki note.",
            EstimatedTokens = 10,
            SemanticSimilarity = 0.44,
            Score = 0.44,
            SourceType = RelevanceSourceType.Wiki
        };

        var knowledgeSearch = Substitute.For<IKnowledgeSearchService>();
        knowledgeSearch.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new List<RelevanceScore> { duplicateVectorA, duplicateVectorB, duplicateVectorDifferentSource }),
                Task.FromResult(new List<RelevanceScore> { duplicateVectorA, duplicateVectorB }));

        var config = Options.Create(new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                MinRelevanceThreshold = 0.0
            }
        });
        var gatekeeper = new ContextGatekeeper(wiki, sessions, knowledgeSearch, config, NullLogger<ContextGatekeeper>.Instance);

        var msg = new LeanKernelMessage { Id = "m1", ChannelId = "test", SenderId = "u1", Content = "is it true?" };
        var ctx = await gatekeeper.GateContextAsync(msg, ContextBudget.FromModelWindow(128_000), "s1", CancellationToken.None);

        Assert.Single(ctx.RetrievedLeanKernels.Where(r =>
            r.SourceType == RelevanceSourceType.Vector &&
            string.Equals(r.EntryId, "who-karen-leung", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(ctx.RetrievedLeanKernels, r =>
            r.SourceType == RelevanceSourceType.Wiki &&
            string.Equals(r.EntryId, "who-karen-leung", StringComparison.OrdinalIgnoreCase));
    }
}
