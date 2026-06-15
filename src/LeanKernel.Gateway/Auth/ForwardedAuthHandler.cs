using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace LeanKernel.Gateway.Auth;

public sealed class ForwardedAuthOptions : AuthenticationSchemeOptions
{
    public bool Enabled { get; set; }
    public bool RequireAuthenticatedUser { get; set; } = true;

    // When true, the handler will only accept X-Auth-Request-User / X-Forwarded-User.
    // This avoids ambiguity when multiple forwarded identity headers are present.
    public bool RequireUserHeader { get; set; } = true;

    // Name of the request header carrying the authenticated user identity.
    // Default checks both oauth2-proxy reverse-proxy (X-Forwarded-User)
    // and forward-auth (X-Auth-Request-User) modes.
    public string UserHeader { get; set; } = "X-Forwarded-User";

    // Fallback header checked when UserHeader yields no value.
    public string? FallbackUserHeader { get; set; } = "X-Auth-Request-User";
}

public sealed class ForwardedAuthHandler : AuthenticationHandler<ForwardedAuthOptions>
{
    public const string SchemeName = "ForwardedAuth";

    public ForwardedAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<ForwardedAuthOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var forwardedUser = Context.Request.Headers[Options.UserHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(forwardedUser) && !string.IsNullOrWhiteSpace(Options.FallbackUserHeader))
        {
            forwardedUser = Context.Request.Headers[Options.FallbackUserHeader].FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(forwardedUser))
        {
            return Authenticate(forwardedUser);
        }

        if (!Options.RequireUserHeader)
        {
            var email = Context.Request.Headers["X-Auth-Request-Email"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(email))
            {
                return Authenticate(email);
            }
        }

        Logger.LogDebug(
            "Forwarded auth identity not found. UserHeader={UserHeader}, FallbackUserHeader={FallbackUserHeader}, RequireAuthenticatedUser={RequireAuthenticatedUser}",
            Options.UserHeader,
            Options.FallbackUserHeader,
            Options.RequireAuthenticatedUser);

        if (Options.RequireAuthenticatedUser)
        {
            return Task.FromResult(AuthenticateResult.Fail("No forwarded auth identity found."));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private Task<AuthenticateResult> Authenticate(string userKey)
    {
        var claims = new[]
        {
            new Claim("sub", userKey),
            new Claim(ClaimTypes.NameIdentifier, userKey),
            new Claim(ClaimTypes.Name, userKey),
            new Claim(ClaimTypes.Email, userKey),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        Context.User = principal;

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
