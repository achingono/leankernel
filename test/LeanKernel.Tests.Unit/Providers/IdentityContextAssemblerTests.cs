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
    public void Build_ReturnsDeterministicAllowlistedBlock()
    {
        var assembler = new IdentityContextAssembler(Options.Create(new IdentityClaimsContextSettings
        {
            PromptFields = ["full_name", "email", "roles", "custom_claims"]
        }));

        var user = new UserEntity
        {
            FullName = "Jane Doe",
            Email = "jane@example.com",
            RolesJson = "[\"admin\",\"reader\"]",
            CustomClaimsJson = "{\"employee_id\":[\"E-100\"],\"cost_center\":[\"RND\"]}"
        };

        var block = assembler.Build(user);

        block.Should().NotBeNull();
        block.Should().Contain("Identity profile (allowlisted):");
        block.Should().Contain("- full_name: Jane Doe");
        block.Should().Contain("- email: jane@example.com");
        block.Should().Contain("- roles: admin, reader");
        block.Should().Contain("- custom.cost_center: RND");
        block.Should().Contain("- custom.employee_id: E-100");
    }

    [Fact]
    public void Build_ReturnsNull_WhenDisabled()
    {
        var assembler = new IdentityContextAssembler(Options.Create(new IdentityClaimsContextSettings
        {
            Enabled = false
        }));

        var block = assembler.Build(new UserEntity { FullName = "Jane Doe" });

        block.Should().BeNull();
    }
}
