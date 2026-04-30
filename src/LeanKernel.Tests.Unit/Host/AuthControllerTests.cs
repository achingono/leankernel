using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Host;

public sealed class AuthControllerTests
{
    private static AuthController CreateController(
        IPasscodeService? passcode = null,
        ITokenService? tokens = null,
        ISecurityStampService? stamp = null,
        LeanKernelConfig? config = null)
    {
        var pc = passcode ?? Substitute.For<IPasscodeService>();
        var tk = tokens ?? Substitute.For<ITokenService>();
        var st = stamp ?? Substitute.For<ISecurityStampService>();
        var cfg = Options.Create(config ?? new LeanKernelConfig());
        var logger = new NullLogger<AuthController>();

        var controller = new AuthController(pc, tk, st, cfg, logger);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task Login_NotConfigured_ReturnsBadRequest()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(false);

        var controller = CreateController(passcode: passcode);
        var result = await controller.Login(new LoginRequest("test"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_WrongPasscode_ReturnsUnauthorized()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(true);
        passcode.VerifyAsync("wrong", Arg.Any<CancellationToken>()).Returns(false);

        var controller = CreateController(passcode: passcode);
        var result = await controller.Login(new LoginRequest("wrong"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_AuthDisabled_ReturnsBadRequest()
    {
        var config = new LeanKernelConfig { Auth = new AuthConfig { Mode = AuthMode.Disabled } };
        var controller = CreateController(config: config);
        var result = await controller.Login(new LoginRequest("any"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetAuthStatus_ReturnsStatus()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(true);

        var controller = CreateController(passcode: passcode);
        var result = controller.GetAuthStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Bootstrap_AlreadyConfigured_ReturnsBadRequest()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(true);

        var controller = CreateController(passcode: passcode);
        var result = await controller.Bootstrap(new BootstrapRequest("newpass123"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Bootstrap_TooShort_ReturnsBadRequest()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(false);

        var controller = CreateController(passcode: passcode);
        var result = await controller.Bootstrap(new BootstrapRequest("short"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Bootstrap_Valid_ReturnsOk()
    {
        var passcode = Substitute.For<IPasscodeService>();
        passcode.IsConfigured.Returns(false);

        var controller = CreateController(passcode: passcode);
        var result = await controller.Bootstrap(new BootstrapRequest("longpasscode123"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await passcode.Received(1).SetAsync("longpasscode123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTokens_ReturnsTokenList()
    {
        var tokens = Substitute.For<ITokenService>();
        tokens.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<ApiToken>().AsReadOnly());

        var controller = CreateController(tokens: tokens);
        var result = await controller.ListTokens(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateToken_EmptyName_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.CreateToken(new CreateTokenRequest(""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RevokeToken_NotFound_ReturnsNotFound()
    {
        var tokens = Substitute.For<ITokenService>();
        tokens.RevokeAsync("tok_bad", Arg.Any<CancellationToken>()).Returns(false);

        var controller = CreateController(tokens: tokens);
        var result = await controller.RevokeToken("tok_bad", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ChangePasscode_TooShort_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.ChangePasscode(
            new ChangePasscodeRequest("current", "short"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
