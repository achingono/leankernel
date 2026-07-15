using FluentAssertions;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Channels;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Channels;

/// <summary>
/// Tests for <see cref="ChannelRouter"/>.
/// </summary>
public sealed class ChannelRouterTests
{
    private static ChannelAuthenticator CreateAuthenticator(ChannelsSettings? settings = null)
        => new(Options.Create(settings ?? new ChannelsSettings()), NullLogger<ChannelAuthenticator>.Instance);

    [Fact]
    public async Task RouteInboundAsync_Disabled_DoesNotRoute()
    {
        var scopeFactory = new TrackingScopeFactory();
        var authenticator = CreateAuthenticator();
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("signal");
        var config = Options.Create(new ChannelsSettings { Enabled = false });

        var router = new ChannelRouter(
            scopeFactory,
            authenticator,
            [channelMock.Object],
            config,
            Mock.Of<ILogger<ChannelRouter>>());

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "user1",
            Content = "hello"
        });

        scopeFactory.CreateScopeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_UnknownChannel_DoesNotAuthorize()
    {
        var scopeFactory = new TrackingScopeFactory();
        var authenticator = CreateAuthenticator();
        var config = Options.Create(new ChannelsSettings { Enabled = true });

        var router = new ChannelRouter(
            scopeFactory,
            authenticator,
            [],
            config,
            Mock.Of<ILogger<ChannelRouter>>());

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "unknown",
            SenderId = "user1",
            Content = "hello"
        });

        scopeFactory.CreateScopeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_UnauthorizedSender_DoesNotRoute()
    {
        var scopeFactory = new TrackingScopeFactory();
        var settings = new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+1234567890"]
                }
            ]
        };
        var authenticator = CreateAuthenticator(settings);
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("signal");
        var config = Options.Create(new ChannelsSettings { Enabled = true });

        var router = new ChannelRouter(
            scopeFactory,
            authenticator,
            [channelMock.Object],
            config,
            Mock.Of<ILogger<ChannelRouter>>());

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "unauthorized",
            Content = "hello"
        });

        scopeFactory.CreateScopeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_AuthorizedSender_RunsRuntimeAndSendsResponse()
    {
        var permit = new TestPermit();
        var identityResolver = new Mock<IIdentityResolver>();
        identityResolver
            .Setup(r => r.ResolveTenantAsync("localhost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntity
            {
                Id = Guid.NewGuid(),
                HostName = "localhost",
                Name = "default",
                Description = "default",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
            });
        identityResolver
            .Setup(r => r.ResolveOrCreateChannelUserAsync(
                It.IsAny<Guid>(),
                "signal",
                "user1",
                "user1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEntity
            {
                Id = Guid.NewGuid(),
                UserName = "user1",
                FullName = "user1",
                IsActive = true,
                IsGuest = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = string.Empty }
            });
        identityResolver
            .Setup(r => r.ResolveOrCreateChannelAsync("signal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelEntity { Id = Guid.NewGuid(), Name = "signal" });

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "runtime response")));

        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("signal");

        var services = new ServiceCollection();
        services.AddScoped<IIdentityResolver>(_ => identityResolver.Object);
        services.AddScoped<IPermit>(_ => permit);
        services.AddScoped<TurnPipeline>(_ => new TurnPipeline([new UserPromptStage()], NullLogger<TurnPipeline>.Instance));
        services.AddScoped<IChatClient>(_ => chatClient.Object);
        services.AddSingleton<ISignalAttachmentParser>(new SignalAttachmentParser(NullLogger<SignalAttachmentParser>.Instance));
        using var provider = services.BuildServiceProvider();

        var scopeFactory = new TrackingScopeFactory(provider);
        var authenticator = CreateAuthenticator(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = false,
                }
            ]
        });
        var config = Options.Create(new ChannelsSettings { Enabled = true });
        var router = new ChannelRouter(
            scopeFactory,
            authenticator,
            [channelMock.Object],
            config,
            Mock.Of<ILogger<ChannelRouter>>());

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "user1",
            Content = "hello from channel"
        });

        identityResolver.Verify(r => r.ResolveOrCreateChannelAsync("signal", It.IsAny<CancellationToken>()), Times.Once);
        identityResolver.Verify(r => r.ResolveOrCreateChannelUserAsync(
            It.IsAny<Guid>(),
            "signal",
            "user1",
            "user1",
            It.IsAny<CancellationToken>()), Times.Once);
        chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(c => c.SendAsync("user1", "runtime response", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_DuplicateChannels_UsesFirstRegistration()
    {
        var scopeFactory = new TrackingScopeFactory();
        var authenticator = CreateAuthenticator();
        var ch1 = new Mock<IChannel>();
        ch1.Setup(c => c.ChannelId).Returns("signal");
        var ch2 = new Mock<IChannel>();
        ch2.Setup(c => c.ChannelId).Returns("signal");
        var config = Options.Create(new ChannelsSettings { Enabled = true });

        var router = new ChannelRouter(
            scopeFactory,
            authenticator,
            [ch1.Object, ch2.Object],
            config,
            Mock.Of<ILogger<ChannelRouter>>());

        router.Should().NotBeNull();
    }

    private sealed class TrackingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public TrackingScopeFactory(IServiceProvider? serviceProvider = null)
        {
            _serviceProvider = serviceProvider ?? new ServiceCollection().BuildServiceProvider();
        }

        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            return new TrackingScope(_serviceProvider);
        }
    }

    private sealed class TrackingScope(IServiceProvider serviceProvider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => serviceProvider;

        public void Dispose()
        {
        }
    }

    private sealed class UserPromptStage : ITurnStage
    {
        public string Name => "UserPrompt";

        public Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
        {
            context.Prompt.Add(new ChatMessage(ChatRole.User, context.UserMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class TestPermit : IPermit
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public Guid TenantId { get; } = Guid.NewGuid();

        public Guid ChannelId { get; } = Guid.NewGuid();

        public string HostName { get; } = "test.local";

        public bool IsAuthenticated { get; } = false;

        public string? SessionId { get; } = "session-1";

        public Badge Badge { get; } = new()
        {
            Id = Guid.NewGuid(),
            FullName = "Test",
            Email = "test@example.com"
        };
    }
}
