using FluentAssertions;
using LeanKernel.Gateway.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Channels;

/// <summary>
/// Tests for <see cref="ChannelAuthenticator"/> — fail-closed per-channel sender authentication.
/// </summary>
public sealed class ChannelAuthenticatorTests
{
    private readonly Mock<ILogger<ChannelAuthenticator>> _loggerMock = new();

    [Fact]
    public void Authorize_NoAuthConfig_ReturnsRejected()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth = []
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+1234567890",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeFalse("because no auth configuration exists for the channel");
        result.Reason.Should().Contain("No auth configuration");
    }

    [Fact]
    public void Authorize_AuthDisabled_ReturnsAuthorized()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = false,
                    AllowedSenders = []
                }
            ]
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "anyone",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeTrue("because authentication is disabled for the channel");
    }

    [Fact]
    public void Authorize_EmptySenderId_ReturnsRejected()
    {
        var config = Options.Create(new ChannelsSettings
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
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeFalse("because sender id is required for authenticated channels");
    }

    [Fact]
    public void Authorize_EmptyAllowlist_ReturnsRejected()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = []
                }
            ]
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+1234567890",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeFalse("because no allowed senders are configured");
    }

    [Fact]
    public void Authorize_SenderNotAllowed_ReturnsRejected()
    {
        var config = Options.Create(new ChannelsSettings
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
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+9999999999",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeFalse("because sender is not in the allowlist");
    }

    [Fact]
    public void Authorize_SenderAllowed_ReturnsAuthorized()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+1234567890", "+9876543210"]
                }
            ]
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+9876543210",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeTrue("because sender is in the allowlist");
    }

    [Fact]
    public void Authorize_CaseInsensitiveMatch_ReturnsAuthorized()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["Sender1"]
                }
            ]
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "sender1",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeTrue("because sender match should be case-insensitive");
    }

    [Fact]
    public void Authorize_ChannelIdCaseInsensitive_ReturnsAuthorized()
    {
        var config = Options.Create(new ChannelsSettings
        {
            ChannelAuth =
            [
                new ChannelAuthSettings
                {
                    ChannelId = "Signal",
                    RequireAuth = true,
                    AllowedSenders = ["+1234567890"]
                }
            ]
        });
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+1234567890",
            Content = "hello"
        };

        var result = authenticator.Authorize(message);

        result.IsAuthorized.Should().BeTrue("because channel id match should be case-insensitive");
    }

    [Fact]
    public void Authorize_NullMessage_Throws()
    {
        var config = Options.Create(new ChannelsSettings());
        var authenticator = new ChannelAuthenticator(config, _loggerMock.Object);

        var act = () => authenticator.Authorize(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
