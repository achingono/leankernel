using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
}
