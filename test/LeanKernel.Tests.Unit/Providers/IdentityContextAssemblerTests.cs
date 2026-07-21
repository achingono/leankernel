using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public class IdentityContextAssemblerTests
{
    [Fact]
    public void Build_ReturnsNull_WhenDisabled()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings { Enabled = false });
        var result = assembler.Build(new UserEntity { FullName = "Test User" });
        result.Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WhenUserIsNull()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings { Enabled = true });
        var result = assembler.Build(null);
        result.Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WhenAllFieldsEmpty()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings
        {
            Enabled = true,
            PromptFields = ["full_name", "email"]
        });
        var result = assembler.Build(new UserEntity());
        result.Should().BeNull();
    }

    [Fact]
    public void Build_RendersScalarFields_Deterministically()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings
        {
            Enabled = true,
            PromptFields = ["full_name", "email", "locale"]
        });
        var user = new UserEntity
        {
            FullName = "Jane Doe",
            Email = "jane@test.com",
            Locale = "en-US"
        };

        var result = assembler.Build(user);

        result.Should().NotBeNull();
        result.Should().Contain("Identity profile (allowlisted):");
        result.Should().Contain("- full_name: Jane Doe");
        result.Should().Contain("- email: jane@test.com");
        result.Should().Contain("- locale: en-US");
    }

    [Fact]
    public void Build_RendersListFields()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings
        {
            Enabled = true,
            PromptFields = ["roles", "groups"]
        });
        var user = new UserEntity
        {
            RolesJson = "[\"admin\", \"user\"]",
            GroupsJson = "[\"engineering\", \"platform\"]"
        };

        var result = assembler.Build(user);

        result.Should().Contain("- roles: admin, user");
        result.Should().Contain("- groups: engineering, platform");
    }

    [Fact]
    public void Build_RendersCustomClaims()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings
        {
            Enabled = true,
            PromptFields = ["custom_claims"]
        });
        var user = new UserEntity
        {
            CustomClaimsJson = "{\"department\": [\"eng\"], \"level\": [\"senior\"]}"
        };

        var result = assembler.Build(user);

        result.Should().Contain("- custom.department: eng");
        result.Should().Contain("- custom.level: senior");
    }

    [Fact]
    public void Build_TruncatesToTokenBudget()
    {
        var assembler = CreateAssembler(new IdentityClaimsContextSettings
        {
            Enabled = true,
            PromptFields = ["full_name"],
            MaxPromptTokens = 1
        });
        var user = new UserEntity { FullName = "Very Long Name That Exceeds Tiny Budget" };

        var result = assembler.Build(user);

        result.Should().NotBeNull();
        result!.Length.Should().BeLessThanOrEqualTo(4);
    }

    private static IdentityContextAssembler CreateAssembler(IdentityClaimsContextSettings settings)
    {
        return new IdentityContextAssembler(Options.Create(settings));
    }
}