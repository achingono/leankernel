using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.Retrieval;

public class RetrievalScopePolicyTests
{
    [Fact]
    public void ResolveScope_prefers_metadata_in_defined_precedence_order()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "Need status",
            SenderId = "user-1",
            ChannelId = "channel-1",
            Metadata = new Dictionary<string, string>
            {
                ["agent_scope"] = "agent-default",
                ["task_scope"] = "task-overlay",
                ["retrieval_scope"] = "personal"
            }
        });

        scope.Should().Be("personal");
    }

    [Fact]
    public void ResolveScope_returns_the_default_scope_when_metadata_is_missing()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "Need status",
            SenderId = "user-1",
            ChannelId = "channel-1"
        });

        scope.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_falls_back_to_the_default_policy_when_scope_is_unknown()
    {
        var policy = CreatePolicy();

        var definition = policy.ResolvePolicy("unknown-scope");

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_returns_null_when_no_default_policy_is_configured()
    {
        var policy = new RetrievalScopePolicy(
            Options.Create(new RetrievalConfig
            {
                DefaultScope = "missing",
                ScopePolicies =
                [
                    new ScopePolicyDefinition { Name = "personal" }
                ]
            }),
            NullLogger<RetrievalScopePolicy>.Instance);

        var definition = policy.ResolvePolicy("unknown-scope");

        definition.Should().BeNull();
    }

    private static RetrievalScopePolicy CreatePolicy()
        => new(
            Options.Create(new RetrievalConfig
            {
                DefaultScope = "global",
                ScopePolicies =
                [
                    new ScopePolicyDefinition { Name = "global" },
                    new ScopePolicyDefinition { Name = "personal" },
                    new ScopePolicyDefinition { Name = "agent-default" },
                    new ScopePolicyDefinition { Name = "task-overlay" }
                ]
            }),
            NullLogger<RetrievalScopePolicy>.Instance);
}
