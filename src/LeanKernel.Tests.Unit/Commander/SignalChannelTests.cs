using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;

namespace LeanKernel.Tests.Unit.Commander;

public class SignalChannelTests
{
    private static Core.Interfaces.IAttachmentTextExtractionService CreateExtractor() =>
        NSubstitute.Substitute.For<Core.Interfaces.IAttachmentTextExtractionService>();

    private static System.Net.Http.IHttpClientFactory CreateHttpClientFactory() =>
        NSubstitute.Substitute.For<System.Net.Http.IHttpClientFactory>();

    [Fact]
    public void SignalChannel_HasCorrectChannelId()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig());
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());
        Assert.Equal("signal", channel.ChannelId);
        Assert.Equal("Signal", channel.Name);
    }

    [Fact]
    public void SignalChannel_IsConfiguredRequiresEnabledAccountAndTransport()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig
            {
                Enabled = true,
                Account = "+15550000000",
                DaemonBaseUrl = "http://localhost:8080"
            }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        Assert.True(channel.IsConfigured);
    }

    [Fact]
    public async Task SignalChannel_DeliverAsync_ReturnsFailureWhenNotConfigured()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig { Enabled = false }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        var result = await channel.DeliverAsync("+recipient", "message");

        Assert.False(result.Success);
        Assert.Equal("Signal channel is disabled", result.Error);
    }

    [Fact]
    public async Task SignalChannel_DisabledConfig_DoesNotThrow()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig { Enabled = false }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        var exception = await Record.ExceptionAsync(async () =>
        {
            await channel.StartAsync(CancellationToken.None);
            await channel.StopAsync(CancellationToken.None);
            await channel.DisposeAsync();
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task SignalChannel_SendAsync_NoAdapter_NoThrow()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig { Enabled = false }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        var exception = await Record.ExceptionAsync(
            () => channel.SendAsync("recipient", "message", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public void SignalChannel_IsAuthorizedSender_NoAllowlist_AllowsAnySender()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig
            {
                Enabled = false,
                AllowedSenders = []
            }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        Assert.True(channel.IsAuthorizedSender("+15550000000"));
    }

    [Fact]
    public void SignalChannel_IsAuthorizedSender_WithAllowlist_RejectsUnknownSender()
    {
        var config = Microsoft.Extensions.Options.Options.Create(new Core.Configuration.LeanKernelConfig
        {
            Signal = new Core.Configuration.SignalConfig
            {
                Enabled = false,
                AllowedSenders = ["+15550001111"]
            }
        });
        var channel = new SignalChannel(
            config,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalChannel>(),
            CreateExtractor(),
            CreateHttpClientFactory());

        Assert.True(channel.IsAuthorizedSender("+15550001111"));
        Assert.False(channel.IsAuthorizedSender("+15559990000"));
    }
}
