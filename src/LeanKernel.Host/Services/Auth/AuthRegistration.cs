using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Extension methods to wire auth services into the DI container and pipeline.
/// </summary>
public static class AuthRegistration
{
    /// <summary>
    /// Represents the add lean kernel auth.
    /// </summary>
    public static IServiceCollection AddLeanKernelAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        string? dataDirectory = null)
    {
        var dataDir = ResolveDataDirectory(configuration, dataDirectory);

        var authStatePath = Path.Combine(dataDir, "auth-state.json");

        // Data Protection — persist keys to data volume
        var keysDir = Path.Combine(dataDir, ".keys");
        Directory.CreateDirectory(keysDir);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
            .SetApplicationName("LeanKernel");

        // Auth state store
        services.AddSingleton<IAuthStateStore>(new AuthStateStore(authStatePath));
        services.AddSingleton<ISecurityStampService, SecurityStampService>();
        services.AddSingleton<IPasscodeService, PasscodeService>();
        services.AddSingleton<ITokenService>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            return new TokenService(
                sp.GetRequiredService<IAuthStateStore>(),
                sp.GetRequiredService<ILogger<TokenService>>(),
                config.Auth.TokenDefaultExpirationDays);
        });

        // Authentication schemes
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = AuthConstants.CookieScheme;
            options.DefaultChallengeScheme = AuthConstants.CookieScheme;
        })
        .AddCookie(AuthConstants.CookieScheme, options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/api/auth/logout";
            options.Cookie.Name = "LEANKERNEL_session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;

            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    var stampService = context.HttpContext.RequestServices
                        .GetRequiredService<ISecurityStampService>();
                    var stampClaim = context.Principal?.FindFirst(AuthConstants.ClaimSecurityStamp)?.Value;
                    if (stampClaim is null || !await stampService.ValidateStampAsync(stampClaim))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(AuthConstants.CookieScheme);
                    }
                },
                OnRedirectToLogin = context =>
                {
                    // API calls get 401 instead of redirect
                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Path.StartsWithSegments("/v1"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }
                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Path.StartsWithSegments("/v1"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                }
            };
        })
        .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthHandler>(
            AuthConstants.BearerScheme, _ => { });

        // Authorization policies
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthConstants.PolicyUiAuthenticated, policy =>
                policy.RequireAuthenticatedUser()
                      .AddAuthenticationSchemes(AuthConstants.CookieScheme)
                      .RequireRole(AuthConstants.RoleAdmin))
            .AddPolicy(AuthConstants.PolicyAdminOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .AddAuthenticationSchemes(AuthConstants.CookieScheme, AuthConstants.BearerScheme)
                      .RequireRole(AuthConstants.RoleAdmin))
            .AddPolicy(AuthConstants.PolicyApiAccess, policy =>
                policy.RequireAuthenticatedUser()
                      .AddAuthenticationSchemes(AuthConstants.BearerScheme)
                      .RequireRole(AuthConstants.RoleApiClient, AuthConstants.RoleAdmin));

        return services;
    }

    private static string ResolveDataDirectory(IConfiguration configuration, string? dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            return dataDirectory;

        return configuration["LeanKernel:Wiki:BasePath"] is string wikiPath
            ? Path.GetDirectoryName(wikiPath) ?? "/app/data"
            : "/app/data";
    }

    /// <summary>
    /// Executes the use lean kernel auth operation.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <returns>The operation result.</returns>
    public static WebApplication UseLeanKernelAuth(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IOptions<LeanKernelConfig>>().Value;

        // Set cookie expiration from config
        app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthConstants.CookieScheme).ExpireTimeSpan =
                TimeSpan.FromMinutes(config.Auth.SessionDurationMinutes);

        // Disable auth enforcement in Development when mode is Disabled
        if (config.Auth.Mode == AuthMode.Disabled)
        {
            if (app.Environment.IsDevelopment())
            {
                app.Logger.LogWarning("Auth is DISABLED — development mode only");
            }
            else
            {
                app.Logger.LogError("Auth mode 'Disabled' is not allowed outside Development. Falling back to LocalPasscode");
            }
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// Creates a ClaimsPrincipal for an authenticated admin session.
    /// </summary>
    public static ClaimsPrincipal CreateAdminPrincipal(string securityStamp, string scheme = AuthConstants.CookieScheme)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, AuthConstants.RoleAdmin),
            new Claim(AuthConstants.ClaimSecurityStamp, securityStamp)
        };
        var identity = new ClaimsIdentity(claims, scheme);
        return new ClaimsPrincipal(identity);
    }
}
