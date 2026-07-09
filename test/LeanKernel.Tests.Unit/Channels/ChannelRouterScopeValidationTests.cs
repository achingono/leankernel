using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelRouterScopeValidationTests
{
    [Fact]
    public void AddLeanKernelChannels_allows_scope_validation_when_router_is_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IAgentRuntime, ScopedAgentRuntime>();
        services.AddScoped<ISessionStore, ScopedSessionStore>();
        services.AddLeanKernelChannels(new ChannelsConfig
        {
            Enabled = true,
            Signal = new SignalChannelConfig { Enabled = false },
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "test",
                    RequireAuth = false,
                }
            ]
        });
        services.AddSingleton<IChannel>(new NoOpChannel("test"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var router = provider.GetRequiredService<IChannelRouter>();
        router.Should().NotBeNull();
    }

    [Fact]
    public async Task RouteInboundAsync_creates_a_fresh_scope_for_each_message()
    {
        ScopedAgentRuntime.SeenScopeIds.Clear();
        ScopedSessionStore.SeenScopeIds.Clear();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IAgentRuntime, ScopedAgentRuntime>();
        services.AddScoped<ISessionStore, ScopedSessionStore>();
        services.AddLeanKernelChannels(new ChannelsConfig
        {
            Enabled = true,
            Signal = new SignalChannelConfig { Enabled = false },
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "test",
                    RequireAuth = false,
                }
            ]
        });

        var channel = new NoOpChannel("test");
        services.AddSingleton<IChannel>(channel);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var router = provider.GetRequiredService<IChannelRouter>();
        await router.RouteInboundAsync(new ChannelMessage { ChannelId = "test", SenderId = "user-1", Content = "hello" });
        await router.RouteInboundAsync(new ChannelMessage { ChannelId = "test", SenderId = "user-1", Content = "hello again" });

        ScopedSessionStore.SeenScopeIds.Should().HaveCount(2);
        ScopedSessionStore.SeenScopeIds[0].Should().NotBe(ScopedSessionStore.SeenScopeIds[1]);
        ScopedAgentRuntime.SeenScopeIds.Should().HaveCount(2);
        ScopedAgentRuntime.SeenScopeIds[0].Should().NotBe(ScopedAgentRuntime.SeenScopeIds[1]);
    }

    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ScopedSessionStore(ScopeMarker marker) : ISessionStore
    {
        public static List<Guid> SeenScopeIds { get; } = [];

        public Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
        {
            SeenScopeIds.Add(marker.Id);
            return Task.FromResult($"session-{marker.Id:N}");
        }

        public Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationTurn>>([]);

        public Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class ScopedAgentRuntime(ScopeMarker marker) : IAgentRuntime
    {
        public static List<Guid> SeenScopeIds { get; } = [];

        public Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default)
            => Task.FromResult("ok");

        public Task<AgentResponse> RunTurnDetailedAsync(LeanKernelMessage message, CancellationToken ct = default)
        {
            SeenScopeIds.Add(marker.Id);
            return Task.FromResult(new AgentResponse { Content = "ok" });
        }
    }

    private sealed class NoOpChannel(string channelId) : IChannel
    {
        public string ChannelId { get; } = channelId;
        public bool IsConnected => true;
        public event Func<ChannelMessage, Task>? MessageReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task StartTypingAsync(string recipientId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopTypingAsync(string recipientId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
