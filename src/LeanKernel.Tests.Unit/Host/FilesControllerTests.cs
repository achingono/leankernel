using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class FilesControllerTests
{
    private static IOptions<LeanKernelConfig> ConfigFor(string tmpDir) =>
        Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = Path.Combine(tmpDir, "wiki") } });

    [Fact]
    public void Browse_Success_ReturnsOk()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_browse_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        try
        {
            var browser = new FileBrowserService(ConfigFor(tmpDir));
            var controller = new FilesController(browser);
            var result = controller.Browse(null);
            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReadFile_Success_ReturnsOk()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_read_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        File.WriteAllText(Path.Combine(tmpDir, "test.txt"), "content");
        try
        {
            var browser = new FileBrowserService(ConfigFor(tmpDir));
            var controller = new FilesController(browser);
            var result = await controller.ReadFile("test.txt", CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReadFile_PathTraversal_ReturnsBadRequest()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_read_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        try
        {
            var browser = new FileBrowserService(ConfigFor(tmpDir));
            var controller = new FilesController(browser);
            var result = await controller.ReadFile("../../etc/passwd", CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
