using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.ToolSelection;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools.ToolSelection;

/// <summary>Exercises agent tool governance and selection during scoped registration.</summary>
public class AgentToolRegistrationTests
{
    [Fact]
    public async Task MissingRegistry_DoesNotAttachTools()
    {
        ChatOptions? options = null;
        var services = CreateServices(value => options = value);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("agent");
        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")]);

        options!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task DisabledTools_DoNotResolveRegistryOrSelector()
    {
        ChatOptions? options = null;
        var services = CreateServices(value => options = value);
        services.PostConfigure<AgentSettings>(settings => settings.Tools.Enabled = false);
        services.AddScoped<IToolRegistry>(_ => throw new InvalidOperationException("registry must not resolve"));
        services.AddScoped<IToolSelector>(_ => throw new InvalidOperationException("selector must not resolve"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("agent");

        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")]);
        options.Should().NotBeNull();
        options!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AtLimit_DoesNotResolveSelector()
    {
        ChatOptions? options = null;
        var services = CreateServices(value => options = value);
        services.PostConfigure<AgentSettings>(settings => settings.Tools.MaxTools = 2);
        services.AddScoped<IToolRegistry>(_ => Registry(Tools("alpha", "bravo")));
        services.AddScoped<IToolSelector>(_ => throw new InvalidOperationException("selector must not resolve"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("agent");

        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")]);
        options!.Tools!.Select(tool => tool.Name).Should().Equal("alpha", "bravo");
    }

    [Fact]
    public async Task GovernanceRunsBeforeSelector()
    {
        ChatOptions? options = null;
        var services = CreateServices(value => options = value);
        services.PostConfigure<AgentSettings>(settings =>
        {
            settings.Tools.MaxTools = 1;
            settings.Tools.AllowedToolNames = ["alpha", "charlie"];
        });
        services.AddScoped<IToolRegistry>(_ => Registry(Tools("alpha", "bravo", "charlie")));
        var selector = new Mock<IToolSelector>();
        selector.Setup(x => x.SelectToolsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ToolDefinition>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<ToolDefinition> tools, int _, CancellationToken _) => [tools[1]]);
        services.AddScoped(_ => selector.Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("agent");

        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")]);
        options!.Tools!.Select(tool => tool.Name).Should().Equal("charlie");
        selector.Verify(x => x.SelectToolsAsync(
            string.Empty,
            It.Is<IReadOnlyList<ToolDefinition>>(tools => tools.Count == 2 && tools[0].Name == "alpha" && tools[1].Name == "charlie"),
            1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrFailingSelector_UsesFirstRegisteredTool(bool throws)
    {
        ChatOptions? options = null;
        var services = CreateServices(value => options = value);
        services.PostConfigure<AgentSettings>(settings => settings.Tools.MaxTools = 1);
        services.AddScoped<IToolRegistry>(_ => Registry(Tools("alpha", "bravo")));
        services.RemoveAll<IToolSelector>();
        if (throws)
        {
            var selector = new Mock<IToolSelector>();
            selector.Setup(x => x.SelectToolsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ToolDefinition>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("unavailable"));
            services.AddScoped(_ => selector.Object);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("agent");

        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")]);
        options!.Tools!.Select(tool => tool.Name).Should().Equal("alpha");
    }

    private static ServiceCollection CreateServices(Action<ChatOptions?> capture)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLeanKernelAgent("agent", new ConfigurationBuilder().Build());
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, options, _) => capture(options))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        services.AddScoped(_ => client.Object);
        services.AddScoped<ChatHistoryProvider>(_ => new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions()));
        return services;
    }

    private static ToolDefinition[] Tools(params string[] names) =>
        names.Select(name => new ToolDefinition { Name = name, Description = name }).ToArray();

    private static IToolRegistry Registry(IReadOnlyList<ToolDefinition> tools)
    {
        var registry = new Mock<IToolRegistry>();
        registry.SetupGet(x => x.Tools).Returns(tools);
        return registry.Object;
    }
}
