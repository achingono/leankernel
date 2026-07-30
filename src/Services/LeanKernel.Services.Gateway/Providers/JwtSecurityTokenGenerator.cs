using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Configuration;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LeanKernel.Services.Gateway.Providers;

/// <summary>
/// Provides a concrete implementation of <see cref="ISecurityTokenGenerator"/> that generates JWT security tokens for authenticated users.
/// </summary>
public class JwtSecurityTokenGenerator : ISecurityTokenGenerator
{
    private SecurityTokenHandler SecurityTokenHandler { get; }

    private IdentitySettings Settings { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtSecurityTokenGenerator"/> class with the specified dependencies.
    /// </summary>
    /// <param name="settings">The identity settings.</param>
    public JwtSecurityTokenGenerator(IOptions<IdentitySettings> settings)
    {
        SecurityTokenHandler = new JwtSecurityTokenHandler();
        Settings = settings.Value;
    }

    private static readonly byte[] DevSecretKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Generates a security token for the specified <see cref="ChannelSenderBindingEntity"/> with an option to make it persistent.
    /// </summary>
    /// <param name="sender">The channel sender binding entity for whom to generate a token.</param>
    /// <param name="isPersistent">A value indicating whether the token should be persistent.</param>
    /// <returns>The generated security token.</returns>
    public string GenerateToken(ChannelSenderBindingEntity sender, bool isPersistent)
    {
        var secretKey = string.IsNullOrWhiteSpace(Settings.Token.SecretKey)
            ? DevSecretKey
            : Encoding.UTF8.GetBytes(Settings.Token.SecretKey);
        var securityKey = new SymmetricSecurityKey(secretKey);
        var signinCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
        var notBefore = DateTime.UtcNow;
        var expires = isPersistent
            ? notBefore.AddDays(Settings.Token.PersistentTimeoutInDays)
            : notBefore.AddMinutes(Settings.Token.TimeoutMinutes);
        var claims = GenerateClaimsWithRights(sender);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = string.IsNullOrWhiteSpace(Settings.Token.Issuer) ? "leankernel-dev" : Settings.Token.Issuer,
            Audience = string.IsNullOrWhiteSpace(Settings.Token.Audience) ? "leankernel-dev" : Settings.Token.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = signinCredentials
        };

        var securityToken = SecurityTokenHandler.CreateToken(tokenDescriptor);

        return SecurityTokenHandler.WriteToken(securityToken);
    }

    /// <summary>
    /// Generate the list of claims associated with the specified <see cref="ChannelSenderBindingEntity"/>.
    /// </summary>
    /// <param name="sender">The <see cref="ChannelSenderBindingEntity"/> to generate claims for.</param>
    /// <returns>An enumeration of <see cref="Claim"/>.</returns>
    public IEnumerable<Claim> GenerateClaimsWithRights(ChannelSenderBindingEntity sender)
    {
        if (sender == null)
        {
            throw new ArgumentNullException(nameof(sender));
        }

        yield return new Claim(ClaimTypes.Sid, sender.User.Id.ToString(), ClaimValueTypes.String);
        yield return new Claim(ClaimTypes.NameIdentifier, sender.User.Email, ClaimValueTypes.Email);
        yield return new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(sender.User.FullName) ? $"{sender.User.FirstName} {sender.User.LastName}" : sender.User.FullName, ClaimValueTypes.String);
        yield return new Claim(ClaimTypes.GivenName, sender.User.FirstName, ClaimValueTypes.String);
        yield return new Claim(ClaimTypes.Surname, sender.User.LastName, ClaimValueTypes.String);
        yield return new Claim(ClaimTypes.Email, sender.User.Email, ClaimValueTypes.Email);

        // Claims required by <see cref="TenantResolutionMiddleware"/> to resolve the tenant for the current request.
        yield return new Claim(TenantResolutionMiddleware.ChannelTenantIdClaimType, sender.Tenant.Id.ToString(), ClaimValueTypes.String);
        yield return new Claim(TenantResolutionMiddleware.ChannelNameClaimType, sender.Channel.Name, ClaimValueTypes.String);
        yield return new Claim(TenantResolutionMiddleware.ChannelSenderIssuerClaimType, sender.Issuer, ClaimValueTypes.String);
        yield return new Claim(TenantResolutionMiddleware.ChannelSenderSubjectClaimType, sender.Subject, ClaimValueTypes.String);

        // Rights for entities used in the normal chat flow and profile management.
        // A user can read, create, and update their own sessions and turns,
        // read telemetry records, view/update their profile, and check memory policies.
        yield return new Claim("right", "Create:SessionEntity");
        yield return new Claim("right", "Read:SessionEntity");
        yield return new Claim("right", "Update:SessionEntity");
        yield return new Claim("right", "Create:TurnEntity");
        yield return new Claim("right", "Read:TurnEntity");
        yield return new Claim("right", "Create:TurnTelemetryEntity");
        yield return new Claim("right", "Read:TurnTelemetryEntity");
        yield return new Claim("right", "Read:UserEntity");
        yield return new Claim("right", "Update:UserEntity");
        yield return new Claim("right", "Read:ChannelMemoryPolicyEntity");
        yield return new Claim("right", "Read:ChannelSenderBindingEntity");
    }
}