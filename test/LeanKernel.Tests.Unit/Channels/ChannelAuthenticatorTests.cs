using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelAuthenticatorTests
{
    [Fact]
    public void Authorize_allows_messages_when_auth_is_disabled_for_the_channel()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = false
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "+15550001"));

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void Authorize_allows_configured_sender_when_auth_is_required()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "+15550001"));

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void Authorize_denies_sender_that_is_not_in_the_allow_list()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "+15550002"));

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Be("Sender is not authorized for channel.");
    }

    [Fact]
    public void Authorize_denies_channels_without_auth_configuration()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig());

        var result = authenticator.Authorize(CreateMessage(channelId: "signal"));

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Be("No auth configuration found for channel.");
    }

    [Fact]
    public void Authorize_throws_on_null_message()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig());

        var act = () => authenticator.Authorize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Authorize_denies_when_sender_id_is_empty_on_authenticated_channel()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: ""));

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Be("Sender id is required for authenticated channels.");
    }

    [Fact]
    public void Authorize_denies_when_sender_id_is_whitespace_on_authenticated_channel()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "   "));

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Be("Sender id is required for authenticated channels.");
    }

    [Fact]
    public void Authorize_denies_when_no_allowed_senders_configured()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = []
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "+15550001"));

        result.IsAuthorized.Should().BeFalse();
        result.Reason.Should().Be("No allowed senders are configured for channel.");
    }

    [Fact]
    public void Authorize_allows_sender_with_case_insensitive_matching()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(senderId: "+15550001"));

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void Authorize_performs_case_insensitive_channel_id_lookup()
    {
        var authenticator = CreateAuthenticator(new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "Signal",
                    RequireAuth = false
                }
            ]
        });

        var result = authenticator.Authorize(CreateMessage(channelId: "signal"));

        result.IsAuthorized.Should().BeTrue();
    }

    private static ChannelAuthenticator CreateAuthenticator(ChannelsConfig config)
        => new(NullLogger<ChannelAuthenticator>.Instance, Options.Create(config));

    private static ChannelMessage CreateMessage(string channelId = "signal", string senderId = "+15550001", string content = "hello")
        => new()
        {
            ChannelId = channelId,
            SenderId = senderId,
            Content = content
        };
}
