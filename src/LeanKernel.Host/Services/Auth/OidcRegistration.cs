using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services.Auth;

/// <summary>
/// Registers OpenID Connect authentication scheme when auth mode is OIDC.
/// </summary>
public static class OidcRegistration
{
    /// <summary>
    /// Executes the add lean kernel oidc operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection AddLeanKernelOidc(this IServiceCollection services, IConfiguration configuration)
    {
        var authConfig = configuration.GetSection("LeanKernel:Auth").Get<AuthConfig>();
        if (authConfig?.Mode != AuthMode.Oidc)
            return services;

        var oidc = authConfig.Oidc;
        if (string.IsNullOrEmpty(oidc.Authority) || string.IsNullOrEmpty(oidc.ClientId))
            return services;

        services.AddAuthentication()
            .AddOpenIdConnect("LeanKernelOidc", options =>
            {
                options.Authority = oidc.Authority;
                options.ClientId = oidc.ClientId;
                options.ClientSecret = oidc.ClientSecret;
                options.CallbackPath = oidc.CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;

                foreach (var scope in oidc.Scopes)
                    options.Scope.Add(scope);

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        var adminClaimType = oidc.AdminClaimType;
                        var adminClaimValue = oidc.AdminSubjectClaim;

                        if (string.IsNullOrEmpty(adminClaimValue))
                        {
                            context.Fail("OIDC admin claim not configured");
                            return Task.CompletedTask;
                        }

                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        var matchClaim = identity?.FindFirst(adminClaimType);

                        if (matchClaim is null ||
                            !string.Equals(matchClaim.Value, adminClaimValue, StringComparison.Ordinal))
                        {
                            context.Fail($"Identity does not match configured admin ({adminClaimType}={adminClaimValue})");
                            return Task.CompletedTask;
                        }

                        // Map to LeanKernel admin role
                        identity!.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.RoleAdmin));

                        // Add security stamp from persistent state
                        var stampService = context.HttpContext.RequestServices
                            .GetRequiredService<ISecurityStampService>();
                        var stamp = stampService.GetStampAsync().GetAwaiter().GetResult();
                        identity.AddClaim(new Claim(AuthConstants.ClaimSecurityStamp, stamp));

                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/login?error=oidc_failed");
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }
}
