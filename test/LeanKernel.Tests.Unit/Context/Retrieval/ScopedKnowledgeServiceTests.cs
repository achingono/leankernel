using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.Retrieval;

public class ScopedKnowledgeServiceTests
{
    [Fact]
    public async Task RetrieveWithScopeAsync_filters_boosts_and_records_diagnostics()
    {
        var knowledge = new TestKnowledgeService();
        knowledge.SearchResponses["Atlas"] =
        [
            CreateCandidate("projects/atlas", 0.25, namespaceValue: "projects", subject: "Atlas", kind: "wiki"),
            CreateCandidate("identity/alice", 0.90, namespaceValue: "identity", subject: "Alice", kind: "profile"),
            CreateCandidate("projects/roadmap", 0.10, namespaceValue: "projects", subject: "Roadmap", kind: "wiki")
        ];
        knowledge.SearchResponses["roadmap"] =
        [
            CreateCandidate("projects/roadmap", 0.10, namespaceValue: "projects", subject: "Roadmap", kind: "wiki")
        ];
        knowledge.Pages["projects/atlas"] = new KnowledgePage
        {
            Key = "projects/atlas",
            Content = "# Atlas",
            LinkedPages = ["projects/roadmap"]
        };
        knowledge.Pages["identity/alice"] = new KnowledgePage
        {
            Key = "identity/alice",
            Content = "# Alice",
            LinkedPages = []
        };
        knowledge.Pages["projects/roadmap"] = new KnowledgePage
        {
            Key = "projects/roadmap",
            Content = "# Roadmap",
            LinkedPages = []
        };

        var service = CreateService(
            knowledge,
            new RetrievalConfig
            {
                DefaultScope = "global",
                MinScopeRelevanceScore = 0.3,
                EntityBoostMultiplier = 1.5,
                EmitRetrievalDiagnostics = true,
                ScopePolicies =
                [
                    new ScopePolicyDefinition
                    {
                        Name = "global",
                        ExcludeNamespaces = ["identity"]
                    }
                ]
            },
            new ContextConfig { EntityExpansionDepth = 1 });

        var result = await service.RetrieveWithScopeAsync("Atlas", "global", 10);

        result.Candidates.Select(candidate => candidate.Key).Should().Equal("projects/atlas");
        result.Candidates[0].Score.Should().BeApproximately(0.375, 0.0001);
        result.Diagnostics.TotalConsidered.Should().Be(3);
        result.Diagnostics.TotalAdmitted.Should().Be(1);
        result.Diagnostics.TotalExcludedByScope.Should().Be(1);
        result.Diagnostics.TotalExcludedByScore.Should().Be(1);
        result.Diagnostics.EffectiveScope.Should().Be("global");
        result.Diagnostics.ExpandedEntities.Should().Contain(entity => entity.Equals("Atlas", StringComparison.OrdinalIgnoreCase));
        result.Diagnostics.Decisions.Single(decision => decision.Key == "projects/atlas").AdjustedScore.Should().BeApproximately(0.375, 0.0001);
        result.Diagnostics.Decisions.Single(decision => decision.Key == "identity/alice").ExclusionReason.Should().Be("out_of_scope_namespace");
        result.Diagnostics.Decisions.Single(decision => decision.Key == "projects/roadmap").ExclusionReason.Should().Be("low_score");
    }

    [Fact]
    public async Task RetrieveWithScopeAsync_enforces_required_metadata_keys()
    {
        var knowledge = new TestKnowledgeService();
        knowledge.SearchResponses["Atlas"] =
        [
            CreateCandidate("projects/atlas", 0.80, namespaceValue: "projects", subject: "Atlas")
        ];

        var service = CreateService(
            knowledge,
            new RetrievalConfig
            {
                DefaultScope = "global",
                MinScopeRelevanceScore = 0.1,
                EntityBoostMultiplier = 1.0,
                MaxEntityExpansionResults = 0,
                ScopePolicies =
                [
                    new ScopePolicyDefinition
                    {
                        Name = "global",
                        IncludeNamespaces = ["projects"],
                        RequiredMetadataKeys = ["kind"]
                    }
                ]
            },
            new ContextConfig());

        var result = await service.RetrieveWithScopeAsync("Atlas", "global", 10);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.TotalExcludedByScope.Should().Be(1);
        result.Diagnostics.Decisions.Should().ContainSingle();
        result.Diagnostics.Decisions[0].ExclusionReason.Should().Be("missing_metadata:kind");
    }

    [Fact]
    public async Task RetrieveWithScopeAsync_returns_empty_when_no_policy_can_be_resolved()
    {
        var knowledge = new TestKnowledgeService();
        knowledge.SearchResponses["Atlas"] =
        [
            CreateCandidate("projects/atlas", 0.80, namespaceValue: "projects", subject: "Atlas", kind: "wiki")
        ];

        var service = CreateService(
            knowledge,
            new RetrievalConfig
            {
                DefaultScope = "missing",
                MaxEntityExpansionResults = 0,
                ScopePolicies = []
            },
            new ContextConfig());

        var result = await service.RetrieveWithScopeAsync("Atlas", "missing", 10);

        result.Candidates.Should().BeEmpty();
        result.Diagnostics.EffectiveScope.Should().Be("missing");
        result.Diagnostics.TotalExcludedByScope.Should().Be(1);
        result.Diagnostics.Decisions.Should().ContainSingle();
        result.Diagnostics.Decisions[0].ExclusionReason.Should().Be("unknown_scope");
    }

    private static ScopedKnowledgeService CreateService(
        IKnowledgeService knowledge,
        RetrievalConfig retrievalConfig,
        ContextConfig contextConfig)
    {
        var scopePolicy = new RetrievalScopePolicy(Options.Create(retrievalConfig), NullLogger<RetrievalScopePolicy>.Instance);
        var expander = new EntityExpander(
            knowledge,
            Options.Create(retrievalConfig),
            Options.Create(contextConfig),
            NullLogger<EntityExpander>.Instance);

        return new ScopedKnowledgeService(
            knowledge,
            scopePolicy,
            expander,
            Options.Create(retrievalConfig),
            NullLogger<ScopedKnowledgeService>.Instance);
    }

    private static RetrievalCandidate CreateCandidate(
        string key,
        double score,
        string source = "gbrain",
        string content = "content",
        string? namespaceValue = null,
        string? subject = null,
        string? kind = null)
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

        if (!string.IsNullOrWhiteSpace(kind))
        {
            metadata["kind"] = kind;
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

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
        {
            var response = SearchResponses.TryGetValue(query, out var candidates)
                ? candidates.Take(maxResults).ToList()
                : [];

            return Task.FromResult<IReadOnlyList<RetrievalCandidate>>(response);
        }

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
        {
            Pages.TryGetValue(key, out var page);
            return Task.FromResult<KnowledgePage?>(page);
        }

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
