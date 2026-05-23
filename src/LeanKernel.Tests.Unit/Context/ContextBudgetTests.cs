using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using LeanKernel.Context.Identity;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context;

public class ContextBudgetTests
{
    [Fact]
    public void FromConfig_uses_headroom_and_default_ratios()
    {
        var budget = ContextBudget.FromConfig(100, new ContextConfig());

        budget.TotalTokens.Should().Be(75);
        budget.SystemPromptBudget.Should().Be(11);
        budget.WikiFactsBudget.Should().Be(15);
        budget.ConversationBudget.Should().Be(30);
        budget.RetrievalBudget.Should().Be(15);
        budget.ToolsBudget.Should().Be(3);
    }

    [Fact]
    public void FromConfig_uses_custom_ratios()
    {
        var budget = ContextBudget.FromConfig(400, new ContextConfig
        {
            ResponseHeadroomRatio = 0.10,
            SystemPromptBudgetRatio = 0.10,
            WikiFactsBudgetRatio = 0.30,
            ConversationBudgetRatio = 0.25,
            RetrievalBudgetRatio = 0.20,
            ToolsBudgetRatio = 0.15,
        });

        budget.TotalTokens.Should().Be(360);
        budget.SystemPromptBudget.Should().Be(36);
        budget.WikiFactsBudget.Should().Be(108);
        budget.ConversationBudget.Should().Be(90);
        budget.RetrievalBudget.Should().Be(72);
        budget.ToolsBudget.Should().Be(54);
    }

    [Fact]
    public void AddLeanKernelContext_registers_expected_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IKnowledgeService>());
        services.AddSingleton(Mock.Of<ISessionStore>());

        services.AddLeanKernelContext(new ContextConfig());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ContextConfig>>().Value.Should().NotBeNull();
        provider.GetRequiredService<IOptions<RetrievalConfig>>().Value.Should().NotBeNull();
        provider.GetRequiredService<IOptions<HistoryConfig>>().Value.EnableCompaction.Should().BeFalse();
        provider.GetRequiredService<IOptions<LiteLlmConfig>>().Value.Should().NotBeNull();
        provider.GetRequiredService<ITokenEstimator>().Should().BeOfType<SimpleTokenEstimator>();
        provider.GetRequiredService<IScopedKnowledgeService>().Should().BeOfType<ScopedKnowledgeService>();
        provider.GetRequiredService<IContextGatekeeper>().Should().BeOfType<ContextGatekeeper>();
    }

    [Fact]
    public void AddLeanKernelIdentity_registers_expected_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IKnowledgeService>());

        services.AddLeanKernelIdentity(new IdentityConfig());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<IdentityConfig>>().Value.Should().NotBeNull();
        provider.GetRequiredService<IIdentityProvider>().Should().BeOfType<IdentityProvider>();
        provider.GetRequiredService<IOnboardingDetector>().Should().BeOfType<OnboardingGapDetector>();
        provider.GetRequiredService<OnboardingDirectiveBuilder>().Should().NotBeNull();
        provider.GetRequiredService<IdentityUpdateProjector>().Should().NotBeNull();
        provider.GetServices<IResponseEnhancer>().Should().BeEmpty();
    }
}
