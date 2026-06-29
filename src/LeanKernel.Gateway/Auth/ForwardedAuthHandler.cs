using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace LeanKernel.Gateway.Auth;

/// <summary>
/// Provides functionality for forwarded auth options.
/// </summary>
public sealed class ForwardedAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Gets or sets enabled.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Gets or sets require authenticated user.
    /// </summary>
    public bool RequireAuthenticatedUser { get; set; } = true;

    // When true, the handler will only accept X-Auth-Request-User / X-Forwarded-User.
    // This avoids ambiguity when multiple forwarded identity headers are present.
    /// <summary>
    /// Gets or sets require user header.
    /// </summary>
    public bool RequireUserHeader { get; set; } = true;

    // Name of the request header carrying the authenticated user identity.
    // Default checks both oauth2-proxy reverse-proxy (X-Forwarded-User)
    // and forward-auth (X-Auth-Request-User) modes.
    /// <summary>
    /// Gets or sets user header.
    /// </summary>
    public string UserHeader { get; set; } = "X-Forwarded-User";

    // Fallback header checked when UserHeader yields no value.
    /// <summary>
    /// Gets or sets fallback user header.
    /// </summary>
    public string? FallbackUserHeader { get; set; } = "X-Auth-Request-User";
}

/// <summary>
/// Provides functionality for forwarded auth handler.
/// </summary>
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
            var forwardedEmail = Context.Request.Headers["X-Forwarded-Email"].FirstOrDefault();
            return Authenticate(forwardedUser, forwardedEmail);
        }

        if (!Options.RequireUserHeader)
        {
            var email = Context.Request.Headers["X-Auth-Request-Email"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(email))
            {
                return Authenticate(email, null);
            }
        }

        Logger.LogInformation(
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

    private Task<AuthenticateResult> Authenticate(string userKey, string? forwardedEmail)
    {
        var email = !string.IsNullOrWhiteSpace(forwardedEmail) ? forwardedEmail : userKey;

        var claims = new[]
        {
            new Claim("sub", userKey),
            new Claim(ClaimTypes.NameIdentifier, email),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        Context.User = principal;

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
