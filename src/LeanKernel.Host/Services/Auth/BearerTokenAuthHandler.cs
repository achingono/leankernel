using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Custom authentication handler for API bearer tokens (Authorization: Bearer sk-LeanKernel-...).
/// </summary>
public sealed class BearerTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITokenService _tokenService;

    public BearerTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawToken))
            return AuthenticateResult.NoResult();

        var token = await _tokenService.VerifyAsync(rawToken);
        if (token is null)
            return AuthenticateResult.Fail("Invalid or expired API token");

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"token:{token.Name}"),
            new Claim(ClaimTypes.NameIdentifier, token.Id),
            new Claim(ClaimTypes.Role, AuthConstants.RoleApiClient),
            new Claim(ClaimTypes.Role, AuthConstants.RoleAdmin)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
