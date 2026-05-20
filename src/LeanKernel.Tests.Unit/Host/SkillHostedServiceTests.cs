using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Host.Services.Skills;
using LeanKernel.Plugins.BuiltIn.Skills;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class SkillHostedServiceTests
{
    [Fact]
    public async Task StartAsync_DoesNotBlockWhenInitializationHangs()
    {
        var registry = Substitute.For<ISkillRegistry>();
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.InitializeAsync(Arg.Any<IEnumerable<string>>()).Returns(neverCompletes.Task);

        var binaryResolver = Substitute.For<IBinaryResolver>();
        var loggerFactory = LoggerFactory.Create(builder => { });
        var factory = new DynamicSkillToolFactory(registry, binaryResolver, loggerFactory);
        var pluginHostLogger = Substitute.For<ILogger<DynamicPluginHost>>();
        var pluginHost = new DynamicPluginHost(factory, [], pluginHostLogger);

        var listeners = Array.Empty<ISkillLifecycleListener>();
        var serviceLogger = Substitute.For<ILogger<SkillHostedService>>();
        var service = new SkillHostedService(registry, pluginHost, listeners, serviceLogger, Array.Empty<string>());

        var sw = Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await service.StopAsync(stopCts.Token);
    }

    [Fact]
    public async Task StartAsync_InitializesAndNotifiesListeners()
    {
        var registry = Substitute.For<ISkillRegistry>();
        registry.InitializeAsync(Arg.Any<IEnumerable<string>>()).Returns(Task.CompletedTask);

        var availableSkill = new SkillDefinition("sample-skill", "sample")
        {
            IsAvailable = true
        };
        var unavailableSkill = new SkillDefinition("missing-skill", "missing")
        {
            IsAvailable = false,
            UnavailableReason = "missing binary"
        };

        var skills = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [availableSkill.Name] = availableSkill,
            [unavailableSkill.Name] = unavailableSkill
        };

        registry.GetAllSkillsAsync().Returns(Task.FromResult<IReadOnlyDictionary<string, SkillDefinition>>(skills));

        var binaryResolver = Substitute.For<IBinaryResolver>();
        var loggerFactory = LoggerFactory.Create(builder => { });
        var factory = new DynamicSkillToolFactory(registry, binaryResolver, loggerFactory);
        var pluginHostLogger = Substitute.For<ILogger<DynamicPluginHost>>();
        var pluginHost = new DynamicPluginHost(factory, [], pluginHostLogger);

        var listener = Substitute.For<ISkillLifecycleListener>();
        var serviceLogger = Substitute.For<ILogger<SkillHostedService>>();
        var service = new SkillHostedService(registry, pluginHost, [listener], serviceLogger, Array.Empty<string>());

        await service.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (listener.ReceivedCalls().Any())
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(listener.ReceivedCalls().Any());

        await listener.Received(1).OnSkillAvailableAsync(
            availableSkill.Name,
            Arg.Is<SkillDefinition>(s => s.Name == availableSkill.Name),
            Arg.Any<CancellationToken>());

        await listener.Received(1).OnSkillUnavailableAsync(
            unavailableSkill.Name,
            unavailableSkill.UnavailableReason!,
            Arg.Any<CancellationToken>());

        await service.StopAsync(CancellationToken.None);
    }
}
