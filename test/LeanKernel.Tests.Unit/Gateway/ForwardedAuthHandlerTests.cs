using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using FluentAssertions;
using LeanKernel.Gateway.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Gateway;

public class ForwardedAuthHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_returns_no_result_when_disabled()
    {
        var result = await AuthenticateAsync(new ForwardedAuthOptions { Enabled = false });

        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_uses_the_fallback_user_header_when_the_primary_header_is_empty()
    {
        var result = await AuthenticateAsync(
            new ForwardedAuthOptions
            {
                Enabled = true,
                UserHeader = "X-Forwarded-User-Primary",
                FallbackUserHeader = "X-Auth-Request-User"
            },
            headers =>
            {
                headers["X-Auth-Request-User"] = "auth-user-42";
                headers["X-Forwarded-Email"] = "user@example.com";
            });

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity!.AuthenticationType.Should().Be(ForwardedAuthHandler.SchemeName);
        result.Principal.Identity!.Name.Should().Be("user@example.com");
        result.Principal.FindFirst("sub")?.Value.Should().Be("auth-user-42");
        result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value.Should().Be("user@example.com");
    }

    [Fact]
    public async Task AuthenticateAsync_uses_the_email_header_when_user_header_is_not_required()
    {
        var result = await AuthenticateAsync(
            new ForwardedAuthOptions
            {
                Enabled = true,
                RequireUserHeader = false
            },
            headers =>
            {
                headers["X-Auth-Request-Email"] = "user@example.com";
            });

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst("sub")?.Value.Should().Be("user@example.com");
        result.Principal.Identity!.Name.Should().Be("user@example.com");
    }

    [Fact]
    public async Task AuthenticateAsync_returns_no_result_when_identity_is_missing_and_authentication_is_not_required()
    {
        var result = await AuthenticateAsync(new ForwardedAuthOptions
        {
            Enabled = true,
            RequireAuthenticatedUser = false
        });

        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_fails_when_identity_is_missing_and_authentication_is_required()
    {
        var result = await AuthenticateAsync(new ForwardedAuthOptions
        {
            Enabled = true,
            RequireAuthenticatedUser = true
        });

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Contain("No forwarded auth identity found.");
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        ForwardedAuthOptions options,
        Action<IHeaderDictionary>? configureHeaders = null)
    {
        var handler = new ForwardedAuthHandler(
            new TestOptionsMonitor(options),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        configureHeaders?.Invoke(context.Request.Headers);

        var scheme = new AuthenticationScheme(
            ForwardedAuthHandler.SchemeName,
            ForwardedAuthHandler.SchemeName,
            typeof(ForwardedAuthHandler));

        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    private sealed class TestOptionsMonitor(ForwardedAuthOptions value) : IOptionsMonitor<ForwardedAuthOptions>
    {
        public ForwardedAuthOptions CurrentValue => value;

        public ForwardedAuthOptions Get(string? name) => value;

        public IDisposable OnChange(Action<ForwardedAuthOptions, string> listener) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
