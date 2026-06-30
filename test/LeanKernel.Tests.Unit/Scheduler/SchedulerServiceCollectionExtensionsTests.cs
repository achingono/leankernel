using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Scheduler;

public class SchedulerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLeanKernelScheduler_registers_services_when_enabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLeanKernelScheduler(new SchedulerConfig { Enabled = true });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<SchedulerConfig>>().Value.Enabled.Should().BeTrue();
        provider.GetRequiredService<CronScheduleEvaluator>().Should().NotBeNull();
        provider.GetRequiredService<TimeBoundaryService>().Should().NotBeNull();
    }

    [Fact]
    public void AddLeanKernelScheduler_does_not_register_services_when_disabled()
    {
        var services = new ServiceCollection();

        services.AddLeanKernelScheduler(new SchedulerConfig { Enabled = false });

        using var provider = services.BuildServiceProvider();
        provider.GetService<IOptions<SchedulerConfig>>().Should().BeNull();
    }

    [Fact]
    public void AddLeanKernelScheduler_throws_on_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddLeanKernelScheduler(new SchedulerConfig()));
    }

    [Fact]
    public void AddLeanKernelScheduler_throws_on_null_config()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddLeanKernelScheduler(null!));
    }
}
