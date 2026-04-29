using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class LogsControllerTests
{
    private static IOptions<LeanKernelConfig> ConfigFor(string logDir) =>
        Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = Path.Combine(Path.GetDirectoryName(logDir)!, "wiki") } });

    [Fact]
    public async Task SearchLogs_ReturnsOk()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_logs_{Guid.NewGuid():N}");
        var logDir = Path.Combine(tmpDir, "logs");
        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        try
        {
            var reader = new LogReaderService(ConfigFor(logDir));
            var controller = new LogsController(reader);
            var result = await controller.SearchLogs(ct: CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void ListFiles_ReturnsOk()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_logs_{Guid.NewGuid():N}");
        var logDir = Path.Combine(tmpDir, "logs");
        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        File.WriteAllText(Path.Combine(logDir, "LeanKernel-20260429.log"), "[12:00:00 INF] Test");
        try
        {
            var reader = new LogReaderService(ConfigFor(logDir));
            var controller = new LogsController(reader);
            var result = controller.ListFiles();
            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchLogs_WithFilter_ReturnsOk()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_logs_{Guid.NewGuid():N}");
        var logDir = Path.Combine(tmpDir, "logs");
        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki"));
        File.WriteAllText(Path.Combine(logDir, "LeanKernel-20260429.log"),
            "[12:00:00 INF] Test info\n[12:00:01 ERR] Test error\n");
        try
        {
            var reader = new LogReaderService(ConfigFor(logDir));
            var controller = new LogsController(reader);
            var result = await controller.SearchLogs(level: "ERR", ct: CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
