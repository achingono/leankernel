using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class ConfigControllerTests
{
    [Fact]
    public void GetConfig_ReturnsOk()
    {
        var config = Options.Create(new LeanKernelConfig());
        var controller = new ConfigController(config);

        var result = controller.GetConfig();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetConfig_MasksApiKey()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "sk-test1234567890" }
        });
        var controller = new ConfigController(config);

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);

        // The response is an anonymous type; check it's not null
        Assert.NotNull(ok.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("sk-test1234567890", json);
        Assert.Contains("sk-t", json); // First 4 chars visible
    }

    [Fact]
    public void GetConfig_ShortApiKey_FullyMasked()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "short" }
        });
        var controller = new ConfigController(config);

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("***", json);
    }

    [Fact]
    public void GetConfig_EmptyApiKey_FullyMasked()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "" }
        });
        var controller = new ConfigController(config);

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("***", json);
    }
}
