using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public sealed class CsvXlsxReadWriteToolTests : IDisposable
{
    private readonly string _root;

    public CsvXlsxReadWriteToolTests()
    {
        _root = Path.Combine(Directory.GetCurrentDirectory(), ".test-artifacts", nameof(CsvXlsxReadWriteToolTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Returns_error_when_operation_missing()
    {
        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["path"] = "data.csv" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Operation is required");
    }

    [Fact]
    public async Task Returns_error_for_path_outside_allowed_root()
    {
        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "../outside.csv",
            ["format"] = "csv"
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Access denied: path is outside the allowed directory");
    }

    [Fact]
    public async Task Writes_csv_from_object_rows_with_deterministic_columns()
    {
        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));
        var rows = JsonSerializer.SerializeToElement(new object[]
        {
            new { z = "last", a = "first" },
            new { a = "second", z = "third" }
        });

        var write = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "out/object-rows.csv",
            ["rows"] = rows
        }, CancellationToken.None);

        write.Success.Should().BeTrue();

        var read = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "out/object-rows.csv"
        }, CancellationToken.None);

        read.Success.Should().BeTrue();
        var payload = JsonDocument.Parse(read.Output!);
        payload.RootElement.GetProperty("columns").EnumerateArray().Select(item => item.GetString()).Should().Equal("a", "z");
        payload.RootElement.GetProperty("rows")[0].EnumerateArray().Select(item => item.GetString()).Should().Equal("first", "last");
    }

    [Fact]
    public async Task Reads_csv_with_max_rows_and_sets_truncated()
    {
        var csvPath = Path.Combine(_root, "data");
        Directory.CreateDirectory(csvPath);
        await File.WriteAllTextAsync(Path.Combine(csvPath, "source.csv"), "id,name\n1,A\n2,B\n3,C\n");

        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));
        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "data/source.csv",
            ["max_rows"] = 2
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = JsonDocument.Parse(result.Output!);
        payload.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
        payload.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Writes_and_reads_xlsx_with_sheet_name()
    {
        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));
        var rows = JsonSerializer.SerializeToElement(new object[]
        {
            new[] { "1", "Ada" },
            new[] { "2", "Grace" }
        });

        var write = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "book/people.xlsx",
            ["sheet"] = "People",
            ["rows"] = rows
        }, CancellationToken.None);

        write.Success.Should().BeTrue();

        var read = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "book/people.xlsx",
            ["sheet"] = "People"
        }, CancellationToken.None);

        read.Success.Should().BeTrue();
        var payload = JsonDocument.Parse(read.Output!);
        payload.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Rejects_append_for_xlsx()
    {
        var tool = CsvXlsxReadWriteTool.Create(CreateScopeFactory(_root));
        var rows = JsonSerializer.SerializeToElement(new object[] { new[] { "a" } });

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "book/out.xlsx",
            ["append"] = true,
            ["rows"] = rows
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Append is only supported for CSV");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IServiceScopeFactory CreateScopeFactory(string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<LeanKernelConfig>(config =>
        {
            config.FileSystem.AllowedRoot = root;
            config.FileSystem.ScratchRoot = Path.Combine(root, ".scratch");
        });

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
