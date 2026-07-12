using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddContextProviders_RegistersChatAndMemoryProviders()
    {
        var services = new ServiceCollection();

        services.AddContextProviders();

        services.Should().Contain(d => d.ServiceType == typeof(Microsoft.Agents.AI.ChatHistoryProvider)
            && d.ImplementationType == typeof(DbChatHistoryProvider));
        services.Should().Contain(d => d.ServiceType == typeof(Microsoft.Agents.AI.AIContextProvider)
            && d.ImplementationType == typeof(MemoryProvider));
    }

    [Fact]
    public void AddLeanKernelChatClient_RegistersKeyedClients()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<OpenAISettings>(o =>
        {
            o.ApiKey = "test-key";
            o.BaseUrl = "https://api.openai.com/v1";
            o.DefaultModel = "gpt-4o-mini";
        });
        services.Configure<SmallModelSettings>(o =>
        {
            o.Enabled = false;
            o.ModelId = "gpt-4o-mini";
        });
        services.Configure<FactExtractionSettings>(o =>
        {
            o.ModelId = "gpt-4o-mini";
        });

        services.AddLeanKernelChatClient();

        using var sp = services.BuildServiceProvider();
        var small = sp.GetRequiredKeyedService<Microsoft.Extensions.AI.IChatClient>("small-model");
        var extraction = sp.GetRequiredKeyedService<Microsoft.Extensions.AI.IChatClient>("fact-extraction");

        small.Should().BeOfType<DisabledChatClient>();
        extraction.Should().NotBeNull();
    }

    [Fact]
    public void AddMemoryPageServices_RegistersPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<SmallModelSettings>(o => o.Enabled = false);
        services.AddKeyedScoped<Microsoft.Extensions.AI.IChatClient>("small-model", (_, _) => new DisabledChatClient());

        services.AddMemoryPageServices();

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<MemoryPageParser>().Should().NotBeNull();
        sp.GetRequiredService<MemoryPageRenderer>().Should().NotBeNull();
        sp.GetRequiredService<MemoryPageNormalizer>().Should().NotBeNull();
        sp.GetRequiredService<IReasoningModel>().Should().NotBeNull();
    }
}
