using FluentAssertions;

using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.ToolSelection;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools.ToolSelection;

/// <summary>Exercises model-assisted tool selection and deterministic fallback.</summary>
public class ToolSelectorTests
{
    private static readonly ToolDefinition[] Tools =
    [
        new() { Name = "alpha", Description = "First\nignored" },
        new() { Name = "bravo", Description = "Second" },
        new() { Name = "charlie", Description = new string('x', 200) },
        new() { Name = "delta", Description = "Fourth" },
    ];

    [Fact]
    public async Task AtCapacity_ReturnsOriginalToolsWithoutCallingModel()
    {
        var client = new Mock<IChatClient>(MockBehavior.Strict);
        using var provider = CreateProvider(client.Object);
        var selector = CreateSelector(provider);

        var selected = await selector.SelectToolsAsync("test", Tools, Tools.Length);

        selected.Should().BeSameAs(Tools);
        client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[\"delta\"]", "delta,alpha")]
    [InlineData("prefix [\"charlie\",\"alpha\",\"delta\",\"alpha\",] suffix", "alpha,charlie")]
    [InlineData("[\"unknown\"]", "alpha,bravo")]
    [InlineData("[]", "alpha,bravo")]
    [InlineData("not json", "alpha,bravo")]
    [InlineData("", "alpha,bravo")]
    public async Task AboveCapacity_SelectsRegisteredNamesOrFallsBack(string responseText, string expected)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        using var provider = CreateProvider(client.Object);
        var selector = CreateSelector(provider);

        var selected = await selector.SelectToolsAsync("test", Tools, 2);

        selected.Select(tool => tool.Name).Should().Equal(expected.Split(','));
    }

    [Fact]
    public async Task MissingClientAndModelFailure_FallBackInRegistrationOrder()
    {
        using var noClient = new ServiceCollection().BuildServiceProvider();
        var selected = await CreateSelector(noClient).SelectToolsAsync("test", Tools, 2);
        selected.Select(tool => tool.Name).Should().Equal("alpha", "bravo");

        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unavailable"));
        using var provider = CreateProvider(client.Object);
        selected = await CreateSelector(provider).SelectToolsAsync("test", Tools, 2);
        selected.Select(tool => tool.Name).Should().Equal("alpha", "bravo");
    }

    [Fact]
    public async Task SelectorTimeout_FallsBackButCallerCancellationPropagates()
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var provider = CreateProvider(client.Object);
        var selector = CreateSelector(provider);
        var selected = await selector.SelectToolsAsync("test", Tools, 2);
        selected.Select(tool => tool.Name).Should().Equal("alpha", "bravo");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await selector.Invoking(x => x.SelectToolsAsync("test", Tools, 2, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CallerCancellationDuringModelCall_Propagates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "[]"));
            });
        using var provider = CreateProvider(client.Object);
        using var cancellation = new CancellationTokenSource();
        var selection = CreateSelector(provider).SelectToolsAsync("test", Tools, 2, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        cancellation.Cancel();

        await selection.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Prompt_UsesBoundedFirstLineDescriptions()
    {
        IEnumerable<ChatMessage>? captured = null;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => captured = messages.ToArray())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "[\"alpha\"]")));
        using var provider = CreateProvider(client.Object);

        await CreateSelector(provider).SelectToolsAsync(new string('z', 600), Tools, 2);

        var prompt = captured!.Last().Text;
        prompt.Should().Contain("- alpha: First").And.NotContain("ignored");
        prompt.Should().Contain(new string('z', 500) + "...").And.NotContain(new string('z', 501));
        prompt.Should().Contain(new string('x', 157) + "...").And.NotContain(new string('x', 158));
    }

    [Fact]
    public async Task RegisteredSelector_UsesScopedSmallModelClient()
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "[\"delta\"]")));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLeanKernelAgent("test-agent", new ConfigurationBuilder().Build());
        services.AddKeyedScoped<IChatClient>("small-model", (_, _) => client.Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var selector = scope.ServiceProvider.GetRequiredService<IToolSelector>();

        var selected = await selector.SelectToolsAsync("test", Tools, 2);

        selected.Select(tool => tool.Name).Should().Equal("delta", "alpha");
        client.Verify(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ServiceProvider CreateProvider(IChatClient client)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("small-model", client);
        return services.BuildServiceProvider();
    }

    private static ToolSelector CreateSelector(IServiceProvider services) =>
        new(services, NullLogger<ToolSelector>.Instance);
}
