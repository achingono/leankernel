using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services.Auth;
using Microsoft.Extensions.Options;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IPasscodeService _passcode;
    private readonly ITokenService _tokens;
    private readonly ISecurityStampService _stamp;
    private readonly IOptions<LeanKernelConfig> _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IPasscodeService passcode,
        ITokenService tokens,
        ISecurityStampService stamp,
        IOptions<LeanKernelConfig> config,
        ILogger<AuthController> logger)
    {
        _passcode = passcode;
        _tokens = tokens;
        _stamp = stamp;
        _config = config;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (_config.Value.Auth.Mode == AuthMode.Disabled)
            return BadRequest(new { error = "Auth is disabled" });

        if (!_passcode.IsConfigured)
            return BadRequest(new { error = "Passcode not configured. Complete onboarding first." });

        if (!await _passcode.VerifyAsync(request.Passcode, ct))
        {
            _logger.LogWarning("Login failed from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid passcode" });
        }

        var stamp = await _stamp.GetStampAsync(ct);
        var principal = AuthRegistration.CreateAdminPrincipal(stamp);
        await HttpContext.SignInAsync(AuthConstants.CookieScheme, principal);

        _logger.LogInformation("Login successful from {IP}", HttpContext.Connection.RemoteIpAddress);
        return Ok(new { message = "Authenticated" });
    }

    [HttpPost("logout")]
    [Authorize(Policy = AuthConstants.PolicyUiAuthenticated)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        _logger.LogInformation("Logout");
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public IActionResult Me()
    {
        return Ok(new
        {
            name = User.Identity?.Name,
            roles = User.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray(),
            authMode = _config.Value.Auth.Mode.ToString()
        });
    }

    [HttpPost("passcode")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> ChangePasscode([FromBody] ChangePasscodeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPasscode) ||
            request.NewPasscode.Length < _config.Value.Auth.Local.MinLength)
        {
            return BadRequest(new { error = $"Passcode must be at least {_config.Value.Auth.Local.MinLength} characters" });
        }

        try
        {
            await _passcode.ChangeAsync(request.CurrentPasscode, request.NewPasscode, ct);
            return Ok(new { message = "Passcode changed. All sessions invalidated." });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Current passcode is incorrect" });
        }
    }

    [HttpGet("tokens")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> ListTokens(CancellationToken ct)
    {
        var tokens = await _tokens.ListAsync(ct);
        return Ok(tokens.Select(t => new
        {
            t.Id,
            t.Name,
            t.CreatedAt,
            t.LastUsedAt,
            t.ExpiresAt,
            t.RevokedAt,
            t.IsValid
        }));
    }

    [HttpPost("tokens")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> CreateToken([FromBody] CreateTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Token name is required" });

        var result = await _tokens.CreateAsync(request.Name, request.ExpirationDays, ct);
        return Ok(new
        {
            result.Token.Id,
            result.Token.Name,
            result.Token.CreatedAt,
            result.Token.ExpiresAt,
            rawToken = result.RawToken,
            message = "Store this token securely — it will not be shown again."
        });
    }

    [HttpDelete("tokens/{id}")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> RevokeToken(string id, CancellationToken ct)
    {
        var revoked = await _tokens.RevokeAsync(id, ct);
        if (!revoked)
            return NotFound(new { error = "Token not found" });

        return Ok(new { message = "Token revoked" });
    }

    [HttpPost("revoke-sessions")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> RevokeSessions(CancellationToken ct)
    {
        await _stamp.RotateStampAsync(ct);
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        return Ok(new { message = "All sessions invalidated" });
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult GetAuthStatus()
    {
        return Ok(new
        {
            mode = _config.Value.Auth.Mode.ToString(),
            configured = _passcode.IsConfigured,
            authenticated = User.Identity?.IsAuthenticated ?? false
        });
    }
}

public sealed record LoginRequest(string Passcode);
public sealed record ChangePasscodeRequest(string CurrentPasscode, string NewPasscode);
public sealed record CreateTokenRequest(string Name, int? ExpirationDays = null);
