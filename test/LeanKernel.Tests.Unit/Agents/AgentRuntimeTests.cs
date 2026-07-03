using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Enhancement;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class AgentRuntimeTests
{
    [Fact]
    public async Task RunTurnAsync_delegates_to_the_pipeline()
    {
        var pipeline = new Mock<ITurnPipeline>(MockBehavior.Strict);
        var message = new LeanKernelMessage
        {
            Content = "Hello",
            SenderId = "user-1",
            ChannelId = "channel-1"
        };

        pipeline
            .Setup(p => p.ProcessAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var runtime = new AgentRuntime(pipeline.Object);

        var response = await runtime.RunTurnAsync(message);

        response.Should().Be("response");
        pipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnDetailedAsync_delegates_to_the_pipeline()
    {
        var pipeline = new Mock<ITurnPipeline>(MockBehavior.Strict);
        var message = new LeanKernelMessage
        {
            Content = "Hello",
            SenderId = "user-1",
            ChannelId = "channel-1"
        };

        pipeline
            .Setup(p => p.ProcessDetailedAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { Content = "response" });

        var runtime = new AgentRuntime(pipeline.Object);

        var response = await runtime.RunTurnDetailedAsync(message);

        response.Content.Should().Be("response");
        pipeline.VerifyAll();
    }

    [Fact]
    public void AgentFactory_test_constructor_exposes_the_supplied_chat_client()
    {
        var chatClient = new Mock<IChatClient>();

        var factory = new AgentFactory(chatClient.Object, NullLogger<AgentFactory>.Instance);

        factory.ChatClient.Should().BeOfType<FunctionInvokingChatClient>();
        factory.GetChatClientForModel(factory.DefaultModel).Should().BeOfType<FunctionInvokingChatClient>();
        factory.DefaultModel.Should().Be(new LiteLlmConfig().DefaultModel);
    }

    [Fact]
    public void AgentFactory_test_constructor_uses_named_model_clients_when_available()
    {
        var defaultClient = new Mock<IChatClient>();
        var premiumClient = new Mock<IChatClient>();
        var factory = new AgentFactory(
            defaultClient.Object,
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>
            {
                ["claude-sonnet-4-20250514"] = premiumClient.Object
            });

        factory.GetChatClientForModel("claude-sonnet-4-20250514").Should().BeOfType<FunctionInvokingChatClient>();
    }

    [Fact]
    public void AgentFactory_test_constructor_throws_for_unknown_named_model()
    {
        var defaultClient = new Mock<IChatClient>();
        var factory = new AgentFactory(defaultClient.Object, NullLogger<AgentFactory>.Instance);

        var act = () => factory.GetChatClientForModel("unknown-model");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddLeanKernelAgents_registers_runtime_services_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddLeanKernelAgents(new LeanKernelConfig());

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ITurnPipeline)
            && descriptor.ImplementationFactory != null
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(TurnPipeline)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IAgentRuntime)
            && descriptor.ImplementationType == typeof(AgentRuntime)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_only_enabled_enhancement_steps()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new LeanKernelConfig
        {
            Enhancement = new EnhancementConfig
            {
                Enabled = true,
                KnowledgeSynthesisEnabled = true,
                RefusalInterceptionEnabled = false,
                CitationInjectionEnabled = true
            }
        };

        services.AddLeanKernelAgents(config);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IResponseEnhancer>().Should().BeOfType<ResponseEnhancementPipeline>();
        provider.GetServices<IEnhancementStep>().Select(step => step.GetType()).Should().Equal(
            typeof(KnowledgeSynthesisStep),
            typeof(CitationInjectionStep));
    }

    [Theory]
    [InlineData(false, false, typeof(StaticAgentStrategy))]
    [InlineData(false, true, typeof(RoutedAgentStrategy))]
    [InlineData(true, false, typeof(OrchestratedAgentStrategy))]
    public void AddLeanKernelAgents_resolves_the_expected_strategy_based_on_config(bool orchestrationEnabled, bool routingEnabled, Type expectedType)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                ApiKey = "test-key"
            },
            Orchestration = new OrchestrationConfig
            {
                Enabled = orchestrationEnabled,
                Workers =
                [
                    new WorkerDefinition
                    {
                        Name = "researcher",
                        Description = "Finds supporting knowledge"
                    }
                ]
            },
            Routing = new RoutingConfig { Enabled = routingEnabled }
        }));
        services.AddSingleton<ITokenEstimator, SimpleTokenEstimator>();
        services.AddSingleton(Mock.Of<IToolRegistry>());
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                ApiKey = "test-key"
            },
            Orchestration = new OrchestrationConfig
            {
                Enabled = orchestrationEnabled,
                Workers =
                [
                    new WorkerDefinition
                    {
                        Name = "researcher",
                        Description = "Finds supporting knowledge"
                    }
                ]
            },
            Routing = new RoutingConfig { Enabled = routingEnabled }
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentStrategy>().GetType().Should().Be(expectedType);
        provider.GetRequiredService<IReadOnlyList<WorkerAgent>>().Should().HaveCount(1);
        provider.GetRequiredService<IResponseQualityGate>().Should().NotBeNull();
    }
}
