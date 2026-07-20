using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.TurnRuntime;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class TurnPipelineServiceExtensionsTests
{
    [Fact]
    public void AddTurnPipeline_BindsTurnPipelineSettingsFromConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TurnPipeline:MaxContextTokens"] = "4096",
                ["TurnPipeline:SystemContextTokenBudget"] = "512",
                ["TurnPipeline:RetrievalTokenBudget"] = "2048",
                ["TurnPipeline:RecentTurnsVerbatim"] = "12",
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTurnPipeline();

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<TurnPipelineSettings>>().Value;

        settings.MaxContextTokens.Should().Be(4096);
        settings.SystemContextTokenBudget.Should().Be(512);
        settings.RetrievalTokenBudget.Should().Be(2048);
        settings.RecentTurnsVerbatim.Should().Be(12);
    }

    [Fact]
    public void AddTurnPipeline_InvalidConfiguration_ThrowsValidationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TurnPipeline:MaxContextTokens"] = "100",
                ["TurnPipeline:SystemContextTokenBudget"] = "200",
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTurnPipeline();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TurnPipelineSettings>>();

        var readOptions = () => _ = options.Value;

        readOptions.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddTurnPipeline_InvalidSummarizationTemperature_ThrowsValidationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TurnPipeline:SummarizationTemperature"] = "2.0",
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTurnPipeline();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TurnPipelineSettings>>();

        var readOptions = () => _ = options.Value;

        readOptions.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddTurnPipeline_RegistersScopedRetrievalStageAsFirstStage()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IMemoryClient>(Mock.Of<IMemoryClient>());
        services.AddSingleton<IEmbeddingClient>(Mock.Of<IEmbeddingClient>());
        services.AddKeyedSingleton<IChatClient>("small-model", Mock.Of<IChatClient>());
        services.Configure<AgentSettings>(s => s.DefaultInstructions = "test");
        services.AddLogging();
        services.AddTurnPipeline();

        using var provider = services.BuildServiceProvider();
        var stages = provider.GetServices<ITurnStage>().ToList();

        stages.Should().NotBeEmpty();
        stages[0].Should().BeOfType<ScopedRetrievalStage>();
    }

    [Fact]
    public void AddTurnPipeline_RegistersAllFourStagesInOrder()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IMemoryClient>(Mock.Of<IMemoryClient>());
        services.AddSingleton<IEmbeddingClient>(Mock.Of<IEmbeddingClient>());
        services.AddKeyedSingleton<IChatClient>("small-model", Mock.Of<IChatClient>());
        services.Configure<AgentSettings>(s => s.DefaultInstructions = "test");
        services.AddLogging();
        services.AddTurnPipeline();

        using var provider = services.BuildServiceProvider();
        var stages = provider.GetServices<ITurnStage>().ToList();

        stages.Should().HaveCount(4);
        stages[0].Should().BeOfType<ScopedRetrievalStage>();
        stages[1].Should().BeOfType<ContextGatekeeper>();
        stages[2].Should().BeOfType<HistoryShaper>();
        stages[3].Should().BeOfType<PromptAssembler>();
    }
}