using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context.Retrieval;

public class RetrievalScopePolicyTests
{
    [Fact]
    public void ResolveScope_throws_ArgumentNullException_when_message_is_null()
    {
        var policy = CreatePolicy();

        var act = () => policy.ResolveScope(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveScope_returns_scope_from_retrieval_scope_metadata()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c",
            Metadata = new Dictionary<string, string>
            {
                ["retrieval_scope"] = "personal"
            }
        });

        scope.Should().Be("personal");
    }

    [Fact]
    public void ResolveScope_returns_scope_from_task_scope_metadata()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c",
            Metadata = new Dictionary<string, string>
            {
                ["task_scope"] = "task-overlay"
            }
        });

        scope.Should().Be("task-overlay");
    }

    [Fact]
    public void ResolveScope_returns_scope_from_agent_scope_metadata()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c",
            Metadata = new Dictionary<string, string>
            {
                ["agent_scope"] = "agent-default"
            }
        });

        scope.Should().Be("agent-default");
    }

    [Fact]
    public void ResolveScope_prefers_retrieval_scope_over_task_scope_over_agent_scope()
    {
        var policy = CreatePolicy();

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c",
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
    public void ResolveScope_returns_configured_default_when_no_scope_metadata_present()
    {
        var configMock = new Mock<IOptions<RetrievalConfig>>();
        configMock.Setup(x => x.Value).Returns(new RetrievalConfig
        {
            DefaultScope = "custom-default"
        });
        var policy = new RetrievalScopePolicy(configMock.Object, NullLogger<RetrievalScopePolicy>.Instance);

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c"
        });

        scope.Should().Be("custom-default");
    }

    [Fact]
    public void ResolveScope_returns_global_when_no_scope_metadata_and_no_configured_default()
    {
        var configMock = new Mock<IOptions<RetrievalConfig>>();
        configMock.Setup(x => x.Value).Returns(new RetrievalConfig
        {
            DefaultScope = ""
        });
        var policy = new RetrievalScopePolicy(configMock.Object, NullLogger<RetrievalScopePolicy>.Instance);

        var scope = policy.ResolveScope(new LeanKernelMessage
        {
            Content = "test",
            SenderId = "u",
            ChannelId = "c"
        });

        scope.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_returns_default_policy_when_scope_is_null()
    {
        var policy = CreatePolicy();

        var definition = policy.ResolvePolicy(null);

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_returns_default_policy_when_scope_is_whitespace()
    {
        var policy = CreatePolicy();

        var definition = policy.ResolvePolicy("   ");

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_returns_matching_policy_for_known_scope()
    {
        var policy = CreatePolicy();

        var definition = policy.ResolvePolicy("personal");

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("personal");
    }

    [Fact]
    public void ResolvePolicy_falls_back_to_default_when_scope_is_unknown()
    {
        var policy = CreatePolicy();

        var definition = policy.ResolvePolicy("unknown-scope");

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("global");
    }

    [Fact]
    public void ResolvePolicy_returns_null_when_unknown_scope_and_no_default_policy_configured()
    {
        var configMock = new Mock<IOptions<RetrievalConfig>>();
        configMock.Setup(x => x.Value).Returns(new RetrievalConfig
        {
            DefaultScope = "missing",
            ScopePolicies =
            [
                new ScopePolicyDefinition { Name = "personal" }
            ]
        });
        var policy = new RetrievalScopePolicy(configMock.Object, NullLogger<RetrievalScopePolicy>.Instance);

        var definition = policy.ResolvePolicy("unknown-scope");

        definition.Should().BeNull();
    }

    private static RetrievalScopePolicy CreatePolicy()
    {
        var configMock = new Mock<IOptions<RetrievalConfig>>();
        configMock.Setup(x => x.Value).Returns(new RetrievalConfig
        {
            DefaultScope = "global",
            ScopePolicies =
            [
                new ScopePolicyDefinition { Name = "global" },
                new ScopePolicyDefinition { Name = "personal" },
                new ScopePolicyDefinition { Name = "agent-default" },
                new ScopePolicyDefinition { Name = "task-overlay" }
            ]
        });

        return new RetrievalScopePolicy(configMock.Object, NullLogger<RetrievalScopePolicy>.Instance);
    }
}
