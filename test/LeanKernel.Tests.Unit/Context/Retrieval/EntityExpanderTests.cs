using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.Retrieval;

public class EntityExpanderTests
{
    [Fact]
    public async Task ExpandAsync_discovers_related_entities_without_duplicates()
    {
        var knowledge = new TestKnowledgeService();
        knowledge.SearchResponses["Atlas"] =
        [
            CreateCandidate("projects/atlas", 0.95, namespaceValue: "projects", subject: "Atlas")
        ];
        knowledge.SearchResponses["alice"] =
        [
            CreateCandidate("people/alice", 0.80, namespaceValue: "people", subject: "Alice")
        ];
        knowledge.Pages["projects/atlas"] = new KnowledgePage
        {
            Key = "projects/atlas",
            Content = "# Atlas",
            LinkedPages = ["people/alice", "people/alice"]
        };
        knowledge.Pages["people/alice"] = new KnowledgePage
        {
            Key = "people/alice",
            Content = "# Alice",
            LinkedPages = ["projects/atlas"]
        };

        var expander = CreateExpander(knowledge, new RetrievalConfig { MaxEntityExpansionResults = 5 }, new ContextConfig { EntityExpansionDepth = 2 });

        var result = await expander.ExpandAsync(
            "Atlas update",
            [CreateCandidate("projects/atlas", 0.95, namespaceValue: "projects", subject: "Atlas")]);

        result.ExpandedCandidates.Select(candidate => candidate.Key).Should().Equal("projects/atlas", "people/alice");
        result.ExpandedEntities.Should().Contain(entity => entity.Equals("Atlas", StringComparison.OrdinalIgnoreCase));
        result.ExpandedEntities.Should().Contain(entity => entity.Equals("alice", StringComparison.OrdinalIgnoreCase));
        result.BoostedCandidateKeys.Should().Contain("projects/atlas");
        result.BoostedCandidateKeys.Should().Contain("people/alice");
        knowledge.PageRequests.Should().Equal("projects/atlas", "people/alice");
    }

    [Fact]
    public async Task ExpandAsync_respects_depth_and_result_limits()
    {
        var knowledge = new TestKnowledgeService();
        knowledge.SearchResponses["Atlas"] =
        [
            CreateCandidate("projects/atlas", 0.95, namespaceValue: "projects", subject: "Atlas"),
            CreateCandidate("projects/atlas-2", 0.70, namespaceValue: "projects", subject: "Atlas")
        ];
        knowledge.Pages["projects/atlas"] = new KnowledgePage
        {
            Key = "projects/atlas",
            Content = "# Atlas",
            LinkedPages = ["people/alice"]
        };

        var expander = CreateExpander(
            knowledge,
            new RetrievalConfig { MaxEntityExpansionResults = 1 },
            new ContextConfig { EntityExpansionDepth = 1 });

        var result = await expander.ExpandAsync(
            "Atlas update",
            [CreateCandidate("projects/atlas", 0.95, namespaceValue: "projects", subject: "Atlas")]);

        result.ExpandedCandidates.Select(candidate => candidate.Key).Should().Equal("projects/atlas");
        knowledge.PageRequests.Should().BeEmpty();
        knowledge.SearchRequests.Should().Contain(request => request.Query.Equals("Atlas", StringComparison.OrdinalIgnoreCase));
        knowledge.SearchRequests.Should().NotContain(request => request.Query.Equals("alice", StringComparison.OrdinalIgnoreCase));
    }

    private static EntityExpander CreateExpander(
        IKnowledgeService knowledge,
        RetrievalConfig retrievalConfig,
        ContextConfig contextConfig)
        => new(
            knowledge,
            Options.Create(retrievalConfig),
            Options.Create(contextConfig),
            NullLogger<EntityExpander>.Instance);

    private static RetrievalCandidate CreateCandidate(
        string key,
        double score,
        string source = "gbrain",
        string content = "content",
        string? namespaceValue = null,
        string? subject = null)
    {
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(namespaceValue))
        {
            metadata["namespace"] = namespaceValue;
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            metadata["subject"] = subject;
        }

        return new RetrievalCandidate
        {
            Key = key,
            Content = content,
            Source = source,
            Score = score,
            TokenCount = 1,
            Metadata = metadata.Count == 0 ? null : metadata
        };
    }

    private sealed class TestKnowledgeService : IKnowledgeService
    {
        public Dictionary<string, List<RetrievalCandidate>> SearchResponses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, KnowledgePage> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Query, int MaxResults)> SearchRequests { get; } = [];

        public List<string> PageRequests { get; } = [];

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
        {
            SearchRequests.Add((query, maxResults));

            var response = SearchResponses.TryGetValue(query, out var candidates)
                ? candidates.Take(maxResults).ToList()
                : [];

            return Task.FromResult<IReadOnlyList<RetrievalCandidate>>(response);
        }

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
        {
            PageRequests.Add(key);
            Pages.TryGetValue(key, out var page);
            return Task.FromResult<KnowledgePage?>(page);
        }

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
