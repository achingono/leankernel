using System.Security.Claims;
using System.Security.Principal;
using FluentAssertions;
using LeanKernel.Channels.Common.Configuration;
using LeanKernel.Gateway;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Memory;
using LeanKernel.Gateway.Requests;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit;

/// <summary>
/// Covers small utility and accessor classes not exercised by other test suites.
/// </summary>
public class MiscCoverageTests
{
    [Fact]
    public void GBrainException_PreservesErrorCode()
    {
        var ex = new GBrainException("test error", 42);
        ex.Message.Should().Be("test error");
        ex.ErrorCode.Should().Be(42);
    }

    [Fact]
    public void GBrainException_DefaultErrorCode_IsZero()
    {
        var ex = new GBrainException("msg");
        ex.ErrorCode.Should().Be(0);
    }

    [Fact]
    public void DisabledChatClient_GetService_ReturnsNull()
    {
        using var client = new DisabledChatClient();
        client.GetService(typeof(object)).Should().BeNull();
    }

    [Fact]
    public void HostNameAccessor_ReturnsRequestHost()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("example.com");
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var accessor = new HostNameAccessor(httpAccessor.Object);

        accessor.HostName.Should().Be("example.com");
    }

    [Fact]
    public void HostNameAccessor_NullContext_ReturnsLocalhost()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var accessor = new HostNameAccessor(httpAccessor.Object);

        accessor.HostName.Should().Be("localhost");
    }

    [Fact]
    public void PrincipalAccessor_ReturnsHttpContextUser()
    {
        var identity = new ClaimsIdentity("Bearer");
        var user = new ClaimsPrincipal(identity);
        var ctx = new DefaultHttpContext { User = user };
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

        var accessor = new PrincipalAccessor(httpAccessor.Object);

        accessor.Principal.Should().BeSameAs(user);
    }

    [Fact]
    public void PrincipalAccessor_NullContext_ReturnsNull()
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var accessor = new PrincipalAccessor(httpAccessor.Object);

        accessor.Principal.Should().BeNull();
    }

    [Fact]
    public void FileSettings_DefaultRootPath_IsEmpty()
    {
        new FileSettings().RootPath.Should().BeEmpty();
    }

    [Fact]
    public void ResolveConnectionString_ReturnsFirstMatch()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = "Data Source=test.db"
            })
            .Build();

        var (name, value) = config.ResolveConnectionString(["Postgres", "SqlServer", "Sqlite"]);

        name.Should().Be("Sqlite");
        value.Should().Be("Data Source=test.db");
    }

    [Fact]
    public void ResolveConnectionString_NoMatch_ReturnsNull()
    {
        var config = new ConfigurationBuilder().Build();

        var (name, value) = config.ResolveConnectionString(["Postgres", "SqlServer", "Sqlite"]);

        name.Should().BeNull();
        value.Should().BeNull();
    }
}
