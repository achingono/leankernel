using LeanKernel.Entities;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Routes inbound channel messages to the agent runtime.
/// Authenticates senders, resolves identity, invokes the turn pipeline,
/// and delivers responses back through the originating channel.
/// </summary>
public sealed class ChannelRouter : IChannelRouter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelAuthenticator _authenticator;
    private readonly ChannelsSettings _config;
    private readonly ILogger<ChannelRouter> _logger;
    private readonly IReadOnlyDictionary<string, IChannel> _channels;
    private readonly TimeProvider _timeProvider;

    public ChannelRouter(
        IServiceScopeFactory scopeFactory,
        ChannelAuthenticator authenticator,
        IEnumerable<IChannel> channels,
        IOptions<ChannelsSettings> config,
        ILogger<ChannelRouter> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;

        var grouped = channels
            .GroupBy(c => c.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var duplicate in grouped.Where(g => g.Count() > 1))
        {
            _logger.LogWarning(
                "Multiple channels registered for {ChannelId}; first registration used",
                duplicate.Key);
        }

        _channels = grouped.ToDictionary(
            g => g.Key,
            g => g.First(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_config.Enabled)
        {
            _logger.LogDebug("Skipping inbound channel message because channels are disabled");
            return;
        }

        if (!_channels.TryGetValue(message.ChannelId, out var channel))
        {
            _logger.LogWarning(
                "Unable to route channel message for {ChannelId} from {SenderId}: no channel adapter registered",
                message.ChannelId, message.SenderId);
            return;
        }

        var authorization = _authenticator.Authorize(message);
        if (!authorization.IsAuthorized)
        {
            _logger.LogWarning(
                "Rejected inbound channel message for {ChannelId} from {SenderId}: {Reason}",
                message.ChannelId, message.SenderId, authorization.Reason);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var identityResolver = scope.ServiceProvider.GetRequiredService<IIdentityResolver>();
        var turnPipeline = scope.ServiceProvider.GetRequiredService<TurnPipeline>();
        var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var signalAttachmentParser = scope.ServiceProvider.GetService<ISignalAttachmentParser>();

        var tenant = await ResolveTenantAsync(identityResolver, ct).ConfigureAwait(false);
        var user = await identityResolver.ResolveOrCreateChannelUserAsync(
            tenant.Id,
            message.ChannelId,
            message.SenderId,
            message.SenderId,
            ct).ConfigureAwait(false);
        var channelEntity = await identityResolver.ResolveOrCreateChannelAsync(message.ChannelId, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Routing inbound channel message for {ChannelId} from {SenderId}",
            message.ChannelId, message.SenderId);

        var turnStartedAt = _timeProvider.GetUtcNow();

        await using var typing = TypingIndicatorKeepAlive.Start(
            channel,
            message.SenderId,
            _config.Typing,
            _timeProvider,
            _logger,
            ct);

        try
        {
            var conversationId = BuildConversationId(message);
            var turnContext = new TurnContext
            {
                Permit = new ChannelPermit(tenant, user, channelEntity),
                UserMessage = message.Content,
                ConversationId = conversationId,
            };

            await turnPipeline.ExecuteAsync(turnContext, ct).ConfigureAwait(false);

            var completion = await chatClient.GetResponseAsync(turnContext.Prompt, cancellationToken: ct).ConfigureAwait(false);
            var outboundText = completion.Messages?
                .FirstOrDefault(m => m.Role == ChatRole.Assistant)?.Text
                ?? completion.Messages?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(outboundText))
            {
                _logger.LogWarning(
                    "Channel runtime returned empty response for {ChannelId} from {SenderId}",
                    message.ChannelId,
                    message.SenderId);
                return;
            }

            IReadOnlyList<Attachment>? outboundAttachments = null;
            if (signalAttachmentParser is not null)
            {
                signalAttachmentParser.TryParseAndRemoveDirective(
                    ref outboundText,
                    out outboundAttachments,
                    message.Attachments);
            }

            await channel.SendAsync(message.SenderId, outboundText, outboundAttachments, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Delivered channel response for {ChannelId} to {SenderId} in {Elapsed}",
                message.ChannelId,
                message.SenderId,
                _timeProvider.GetUtcNow() - turnStartedAt);
        }
        finally
        {
            await typing.StopAsync().ConfigureAwait(false);
        }
    }

    private static string BuildConversationId(ChannelMessage message)
        => $"{message.ChannelId}:{message.SenderId}";

    private async Task<TenantEntity> ResolveTenantAsync(IIdentityResolver identityResolver, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.TenantHostName))
        {
            throw new InvalidOperationException("Channels:TenantHostName must be configured for channel routing.");
        }

        var tenant = await identityResolver.ResolveTenantAsync(_config.TenantHostName, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            throw new InvalidOperationException($"No active tenant found for Channels:TenantHostName '{_config.TenantHostName}'.");
        }

        return tenant;
    }

    private sealed class ChannelPermit(TenantEntity tenant, UserEntity user, ChannelEntity channel) : IPermit
    {
        public Guid UserId { get; } = user.Id;

        public Guid TenantId { get; } = tenant.Id;

        public Guid ChannelId { get; } = channel.Id;

        public string HostName { get; } = tenant.HostName;

        public bool IsAuthenticated => false;

        public string? SessionId => null;

        public Badge Badge { get; } = new()
        {
            Id = user.Id,
            FullName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName,
            Email = user.Email
        };
    }
}
