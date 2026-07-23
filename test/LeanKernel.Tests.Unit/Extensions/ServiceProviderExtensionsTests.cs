using System.Reflection;

using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Extensions;

public sealed class ServiceProviderExtensionsTests
{
    private static readonly MethodInfo RegisterDocumentToolsAsyncMethod =
        typeof(IServiceProviderExtensions).GetMethod("RegisterDocumentToolsAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IServiceProviderExtensions.RegisterDocumentToolsAsync was not found.");

    [Fact]
    public async Task RegisterDocumentToolsAsync_WhenEnabled_RegistersDocumentTools()
    {
        var registry = new ToolRegistry();
        using var provider = BuildProvider(documentIngestionEnabled: true);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        await InvokeRegisterDocumentToolsAsync(
            registry,
            provider,
            scopeFactory,
            CancellationToken.None);

        registry.Contains("document_search").Should().BeTrue();
        registry.Contains("document_list").Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDocumentToolsAsync_WhenDisabled_DoesNotRegisterDocumentTools()
    {
        var registry = new ToolRegistry();
        using var provider = BuildProvider(documentIngestionEnabled: false);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        await InvokeRegisterDocumentToolsAsync(
            registry,
            provider,
            scopeFactory,
            CancellationToken.None);

        registry.Contains("document_search").Should().BeFalse();
        registry.Contains("document_list").Should().BeFalse();
    }

    [Fact]
    public async Task RegisterDocumentToolsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var registry = new ToolRegistry();
        using var provider = BuildProvider(documentIngestionEnabled: true);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => InvokeRegisterDocumentToolsAsync(
            registry,
            provider,
            scopeFactory,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceProvider BuildProvider(bool documentIngestionEnabled)
    {
        var settings = new AgentSettings
        {
            Tools = new ToolSettings
            {
                DocumentIngestion = new DocumentIngestionToolSettings
                {
                    Enabled = documentIngestionEnabled,
                },
            },
        };

        return new ServiceCollection()
            .AddSingleton<IOptions<AgentSettings>>(Options.Create(settings))
            .BuildServiceProvider();
    }

    private static async Task InvokeRegisterDocumentToolsAsync(
        IToolRegistry registry,
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        var task = (Task)RegisterDocumentToolsAsyncMethod.Invoke(
            null,
            [registry, services, scopeFactory, NullLogger.Instance, cancellationToken])!;

        await task;
    }
}
