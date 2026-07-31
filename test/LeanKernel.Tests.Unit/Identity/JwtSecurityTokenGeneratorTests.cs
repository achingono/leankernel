using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Configuration;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Unit tests for <see cref="JwtSecurityTokenGenerator"/> covering token generation,
/// claim production, dev-secret fallback, and persistent/non-persistent expiry.
/// </summary>
public class JwtSecurityTokenGeneratorTests
{
    private static ChannelSenderBindingEntity CreateTestSender(bool withFullName = true)
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        return new ChannelSenderBindingEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Issuer = "signal",
            Subject = "+15551234",
            Tenant = new TenantEntity
            {
                Id = tenantId,
                Name = "TestTenant",
                HostName = "test.example.com",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
            },
            User = new UserEntity
            {
                Id = userId,
                Email = "user@test.com",
                FirstName = "Jane",
                LastName = "Doe",
                FullName = withFullName ? "Jane Doe" : string.Empty,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "system", Email = string.Empty }
            },
            Channel = new ChannelEntity
            {
                Id = channelId,
                Name = "signal"
            }
        };
    }

    private static JwtSecurityTokenGenerator CreateGenerator(IdentitySettings? settings = null)
    {
        return new JwtSecurityTokenGenerator(Options.Create(settings ?? new IdentitySettings()));
    }

    [Fact]
    public void GenerateToken_WithDevSecretKey_ProducesValidToken()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings()
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be("leankernel-dev");
        jwt.Audiences.Should().Contain("leankernel-dev");
    }

    [Fact]
    public void GenerateToken_WithConfiguredSecretKey_ProducesValidToken()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings
            {
                SecretKey = "super-secret-key-32-bytes!!-min-32-bytes",
                Issuer = "my-issuer",
                Audience = "my-audience",
            }
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be("my-issuer");
        jwt.Audiences.Should().Contain("my-audience");
    }

    [Fact]
    public void GenerateToken_NonPersistent_HasShortExpiry()
    {
        var sender = CreateTestSender();
        var settings = new IdentitySettings
        {
            Token = new TokenSettings
            {
                SecretKey = "super-secret-key-32-bytes!!-min-32-bytes",
                TimeoutMinutes = 30,
                PersistentTimeoutInDays = 365,
            }
        };
        var generator = CreateGenerator(settings);

        var token = generator.GenerateToken(sender, isPersistent: false);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var expectedExpiry = DateTime.UtcNow.AddMinutes(settings.Token.TimeoutMinutes);
        var expectedExpirySeconds = new DateTimeOffset(expectedExpiry).ToUnixTimeSeconds();
        var actualExpirySeconds = (double?)jwt.Payload.Expiration;
        actualExpirySeconds.Should().BeApproximately(
            (double)expectedExpirySeconds,
            5.0,
            because: "non-persistent tokens expire after TimeoutMinutes");
    }

    [Fact]
    public void GenerateToken_Persistent_HasLongExpiry()
    {
        var sender = CreateTestSender();
        var settings = new IdentitySettings
        {
            Token = new TokenSettings
            {
                SecretKey = "super-secret-key-32-bytes!!-min-32-bytes",
                TimeoutMinutes = 30,
                PersistentTimeoutInDays = 365,
            }
        };
        var generator = CreateGenerator(settings);

        var token = generator.GenerateToken(sender, isPersistent: true);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var expectedExpiry = DateTime.UtcNow.AddDays(settings.Token.PersistentTimeoutInDays);
        var expectedExpirySeconds = new DateTimeOffset(expectedExpiry).ToUnixTimeSeconds();
        var actualExpirySeconds = (double?)jwt.Payload.Expiration;
        actualExpirySeconds.Should().BeApproximately(
            (double)expectedExpirySeconds,
            5.0,
            because: "persistent tokens expire after PersistentTimeoutInDays");
    }

    [Fact]
    public void GenerateToken_ProducesExpectedClaims()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings { SecretKey = "super-secret-key-32-bytes!!-min-32-bytes" }
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Sid && c.Value == sender.User.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "nameid" && c.Value == sender.User.Email);
        jwt.Claims.Should().Contain(c => c.Type == "unique_name" && c.Value == "Jane Doe");
        jwt.Claims.Should().Contain(c => c.Type == "given_name" && c.Value == "Jane");
        jwt.Claims.Should().Contain(c => c.Type == "family_name" && c.Value == "Doe");
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == sender.User.Email);

        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.ChannelTenantId && c.Value == sender.Tenant.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.ChannelName && c.Value == sender.Channel.Name);
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.ChannelSenderIssuer && c.Value == sender.Issuer);
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.ChannelSenderSubject && c.Value == sender.Subject);

        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Create:SessionEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:SessionEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Update:SessionEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Create:TurnEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:TurnEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Create:TurnTelemetryEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:TurnTelemetryEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:UserEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Update:UserEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:ChannelMemoryPolicyEntity");
        jwt.Claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:ChannelSenderBindingEntity");
    }

    [Fact]
    public void GenerateToken_WithNullFullName_ConstructsFromFirstAndLastName()
    {
        var sender = CreateTestSender(withFullName: false);
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings { SecretKey = "super-secret-key-32-bytes!!-min-32-bytes" }
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "unique_name" && c.Value == "Jane Doe");
    }

    [Fact]
    public void GenerateClaimsWithRights_NullSender_ThrowsArgumentNullException()
    {
        var generator = CreateGenerator();

        var act = () => generator.GenerateClaimsWithRights(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("sender");
    }

    [Fact]
    public void GenerateClaimsWithRights_WithValidSender_ReturnsAllExpectedClaims()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator();

        var claims = generator.GenerateClaimsWithRights(sender).ToList();

        claims.Should().Contain(c => c.Type == ClaimTypes.Sid && c.Value == sender.User.Id.ToString());
        claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == sender.User.Email);
        claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == sender.User.Email);
        claims.Should().Contain(c => c.Type == Constants.Claims.ChannelTenantId && c.Value == sender.Tenant.Id.ToString());
        claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Create:SessionEntity");
        claims.Should().Contain(c => c.Type == Constants.Claims.Right && c.Value == "Read:ChannelSenderBindingEntity");
    }

    [Fact]
    public void GenerateToken_WithWhitespaceSecretKey_UsesDevKey()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings { SecretKey = "   " }
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Sid);
    }

    [Fact]
    public void GenerateToken_WithNullIssuer_UsesDevIssuer()
    {
        var sender = CreateTestSender();
        var generator = CreateGenerator(new IdentitySettings
        {
            Token = new TokenSettings { SecretKey = "super-secret-key-32-bytes!!-min-32-bytes", Issuer = null!, Audience = null! }
        });

        var token = generator.GenerateToken(sender, isPersistent: false);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be("leankernel-dev");
        jwt.Audiences.Should().Contain("leankernel-dev");
    }
}
