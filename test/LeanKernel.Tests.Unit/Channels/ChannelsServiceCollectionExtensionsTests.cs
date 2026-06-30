using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLeanKernelChannels_registers_services_as_singletons()
    {
        var services = new ServiceCollection();

        services.AddLeanKernelChannels(new ChannelsConfig
        {
            Signal = new SignalChannelConfig { Enabled = false }
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IOptions<ChannelsConfig>) &&
            sd.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IChannelRouter) &&
            sd.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ChannelAuthenticator) &&
            sd.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(sd =>
            sd.ImplementationType == typeof(ChannelHostedService));
    }

    [Fact]
    public void AddLeanKernelChannels_does_not_register_signal_channel_when_disabled()
    {
        var services = new ServiceCollection();

        services.AddLeanKernelChannels(new ChannelsConfig
        {
            Signal = new SignalChannelConfig { Enabled = false }
        });

        services.Should().NotContain(sd => sd.ServiceType == typeof(IChannel));
    }

    [Fact]
    public void AddLeanKernelChannels_registers_signal_channel_and_http_client_when_enabled()
    {
        var services = new ServiceCollection();

        services.AddLeanKernelChannels(new ChannelsConfig
        {
            Signal = new SignalChannelConfig { Enabled = true, DaemonUrl = "http://localhost:8080" }
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IChannel) &&
            sd.ImplementationType == typeof(SignalChannel));
    }

    [Fact]
    public void AddLeanKernelChannels_throws_on_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddLeanKernelChannels(new ChannelsConfig()));
    }

    [Fact]
    public void AddLeanKernelChannels_throws_on_null_config()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddLeanKernelChannels(null!));
    }
}
