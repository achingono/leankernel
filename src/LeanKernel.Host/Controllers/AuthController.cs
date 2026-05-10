using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services.Auth;
using Microsoft.Extensions.Options;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the auth controller.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IPasscodeService _passcode;
    private readonly ITokenService _tokens;
    private readonly ISecurityStampService _stamp;
    private readonly IOptions<LeanKernelConfig> _config;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Represents the auth controller.
    /// </summary>
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

    /// <summary>
    /// Executes the login operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

    /// <summary>
    /// Executes the login form operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpPost("login-form")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginForm([FromForm] LoginFormRequest request, CancellationToken ct)
    {
        if (_config.Value.Auth.Mode == AuthMode.Disabled)
            return Redirect("/login?error=auth_disabled");

        if (!_passcode.IsConfigured)
            return Redirect("/login?error=not_configured");

        if (!await _passcode.VerifyAsync(request.Passcode, ct))
        {
            _logger.LogWarning("Login failed from {IP}", HttpContext.Connection.RemoteIpAddress);
            return Redirect($"/login?error=invalid_passcode&returnUrl={Uri.EscapeDataString(NormalizeReturnUrl(request.ReturnUrl))}");
        }

        var stamp = await _stamp.GetStampAsync(ct);
        var principal = AuthRegistration.CreateAdminPrincipal(stamp);
        await HttpContext.SignInAsync(AuthConstants.CookieScheme, principal);

        _logger.LogInformation("Login successful from {IP}", HttpContext.Connection.RemoteIpAddress);
        return LocalRedirect(NormalizeReturnUrl(request.ReturnUrl));
    }

    /// <summary>
    /// Executes the logout operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpPost("logout")]
    [Authorize(Policy = AuthConstants.PolicyUiAuthenticated)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        _logger.LogInformation("Logout");
        return Ok(new { message = "Logged out" });
    }

    /// <summary>
    /// Executes the me operation.
    /// </summary>
    /// <returns>The operation result.</returns>
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

    /// <summary>
    /// Executes the change passcode operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

    /// <summary>
    /// Executes the list tokens operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

    /// <summary>
    /// Executes the create token operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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

    /// <summary>
    /// Executes the revoke token operation.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpDelete("tokens/{id}")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> RevokeToken(string id, CancellationToken ct)
    {
        var revoked = await _tokens.RevokeAsync(id, ct);
        if (!revoked)
            return NotFound(new { error = "Token not found" });

        return Ok(new { message = "Token revoked" });
    }

    /// <summary>
    /// Executes the revoke sessions operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpPost("revoke-sessions")]
    [Authorize(Policy = AuthConstants.PolicyAdminOnly)]
    public async Task<IActionResult> RevokeSessions(CancellationToken ct)
    {
        await _stamp.RotateStampAsync(ct);
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        return Ok(new { message = "All sessions invalidated" });
    }

    /// <summary>
    /// Executes the get auth status operation.
    /// </summary>
    /// <returns>The operation result.</returns>
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

    /// <summary>
    /// One-time bootstrap endpoint: sets initial passcode when none exists.
    /// Only works during onboarding (no passcode configured yet).
    /// </summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest request, CancellationToken ct)
    {
        if (_passcode.IsConfigured)
            return BadRequest(new { error = "Passcode already configured. Use the change endpoint instead." });

        if (string.IsNullOrWhiteSpace(request.Passcode) ||
            request.Passcode.Length < _config.Value.Auth.Local.MinLength)
        {
            return BadRequest(new { error = $"Passcode must be at least {_config.Value.Auth.Local.MinLength} characters" });
        }

        await _passcode.SetAsync(request.Passcode, ct);
        _logger.LogInformation("Initial passcode set via bootstrap");
        return Ok(new { message = "Passcode configured successfully" });
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            return "/";
        return returnUrl;
    }
}

/// <summary>
/// Represents the login request.
/// </summary>
public sealed record LoginRequest(string Passcode);
/// <summary>
/// Represents the login form request.
/// </summary>
public sealed record LoginFormRequest(string Passcode, string? ReturnUrl);
/// <summary>
/// Represents the change passcode request.
/// </summary>
public sealed record ChangePasscodeRequest(string CurrentPasscode, string NewPasscode);
/// <summary>
/// Represents the create token request.
/// </summary>
public sealed record CreateTokenRequest(string Name, int? ExpirationDays = null);
/// <summary>
/// Represents the bootstrap request.
/// </summary>
public sealed record BootstrapRequest(string Passcode);
