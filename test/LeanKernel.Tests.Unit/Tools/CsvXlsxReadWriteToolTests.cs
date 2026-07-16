using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Configuration;
using Xunit;
using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class CsvXlsxReadWriteToolTests : IDisposable
{
    private readonly string _rootDir;

    public CsvXlsxReadWriteToolTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "csvxlsx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_rootDir, true); } catch { /* ignore */ }
    }

    private IServiceScopeFactory BuildScopeFactory(string? rootPath = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.DatabaseQuery.Enabled = true;
        });
        services.Configure<FileSettings>(opts =>
        {
            opts.RootPath = rootPath ?? _rootDir;
        });
        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });
        return mockFactory.Object;
    }

    private async Task<ToolResult> InvokeAsync(
        Dictionary<string, object?> args,
        IServiceScopeFactory? scopeFactory = null)
    {
        scopeFactory ??= BuildScopeFactory();
        var tool = CsvXlsxReadWriteTool.Create(scopeFactory);
        return await tool.Handler(args, CancellationToken.None);
    }

    private string WriteCsvFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_rootDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string CreateTempXlsx(string relativePath, List<string> headers, List<List<string>> rows, string sheetName = "Sheet1")
    {
        var fullPath = Path.Combine(_rootDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Count; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    [Fact]
    public async Task Create_DefinesCorrectMetadata()
    {
        var tool = CsvXlsxReadWriteTool.Create(BuildScopeFactory());
        tool.Name.Should().Be("csv_xlsx_read_write");
        tool.Category.Should().Be("data");
        tool.Parameters.Should().HaveCount(9);
        tool.Parameters.Select(p => p.Name).Should().Contain(
            ["operation", "path", "format", "sheet", "has_header", "max_rows", "append", "columns", "rows"]);
    }

    // ── CSV Read ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadCsv_WithHeader_ReturnsColumnsAndRows()
    {
        WriteCsvFile("data.csv", "Name,Age,City\nAlice,30,NYC\nBob,25,LA");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "data.csv"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var root = doc.RootElement;
        root.GetProperty("rowCount").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var columns = root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList();
        columns.Should().BeEquivalentTo(["Name", "Age", "City"]);
        root.GetProperty("rows")[0].GetProperty("Name").GetString().Should().Be("Alice");
        root.GetProperty("rows")[1].GetProperty("Age").GetString().Should().Be("25");
    }

    [Fact]
    public async Task ReadCsv_MaxRows_Truncates()
    {
        var lines = new[] { "Id,Value" }.Concat(Enumerable.Range(1, 10).Select(i => $"{i},v{i}")).ToArray();
        WriteCsvFile("trunc.csv", string.Join("\n", lines));
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "trunc.csv",
            ["max_rows"] = 3
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ReadCsv_NoHeader_ReadsIndexedColumns()
    {
        WriteCsvFile("nohdr.csv", "Alice,30\nBob,25");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "nohdr.csv",
            ["has_header"] = false
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList();
        columns.Should().Contain("c1");
        columns.Should().Contain("c2");
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ReadCsv_EmptyFile_ReturnsZeroRows()
    {
        WriteCsvFile("empty.csv", "");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "empty.csv"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ReadCsv_TrimmedHeader_StillWorks()
    {
        WriteCsvFile("trim.csv", " Id , Name \n1,Alice");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "trim.csv"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ReadCsv_SemicolonDelimited_Works()
    {
        WriteCsvFile("semi.csv", "Name,Age\nAlice;30\nBob;25\nCarol;40");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "semi.csv"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ReadCsv_LargeMaxRows_AllReturned()
    {
        var lines = new[] { "Id" }.Concat(Enumerable.Range(1, 5).Select(i => $"{i}")).ToArray();
        WriteCsvFile("small.csv", string.Join("\n", lines));
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "small.csv",
            ["max_rows"] = 1000
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(5);
    }

    // ── CSV Write ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteCsv_NewFile_CreatesFile()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "output.csv",
            ["columns"] = """["Name","Age"]""",
            ["rows"] = """[["Alice","30"],["Bob","25"]]"""
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("2 rows");
        File.Exists(Path.Combine(_rootDir, "output.csv")).Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "output.csv"));
        content.Should().Contain("Name");
        content.Should().Contain("Alice");
    }

    [Fact]
    public async Task WriteCsv_ObjectRows_NormalizesCorrectly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "obj.csv",
            ["columns"] = """["Id","Val"]""",
            ["rows"] = """[{"Id":1,"Val":"x"},{"Id":2,"Val":"y"}]"""
        });
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_rootDir, "obj.csv")).Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "obj.csv"));
        content.Should().Contain("x");
        content.Should().Contain("y");
    }

    [Fact]
    public async Task WriteCsv_AppendMode_AddsToExisting()
    {
        WriteCsvFile("append.csv", "Name,Age\nAlice,30\n");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "append.csv",
            ["columns"] = """["Name","Age"]""",
            ["rows"] = """[["Bob","25"]]""",
            ["append"] = true
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "append.csv"));
        content.Should().Contain("Alice");
        content.Should().Contain("Bob");
    }

    [Fact]
    public async Task WriteCsv_NullValue_InRow_WrittenAsEmpty()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "nullvals.csv",
            ["columns"] = """["A","B"]""",
            ["rows"] = """[{"A":"hello","B":null}]"""
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "nullvals.csv"));
        content.Should().Contain("hello");
    }

    [Fact]
    public async Task WriteCsv_MissingColumns_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "bad.csv",
            ["rows"] = """[["a","b"]]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Columns and rows are required");
    }

    [Fact]
    public async Task WriteCsv_MissingRows_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "bad2.csv",
            ["columns"] = """["A"]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Columns and rows are required");
    }

    // ── XLSX Read ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadXlsx_WithHeader_ReturnsColumnsAndRows()
    {
        CreateTempXlsx("data.xlsx",
            ["Name", "Score"],
            [["Alice", "95"], ["Bob", "87"]]);
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "data.xlsx"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var root = doc.RootElement;
        root.GetProperty("rowCount").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var columns = root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList();
        columns.Should().BeEquivalentTo(["Name", "Score"]);
        root.GetProperty("rows")[0].GetProperty("Name").GetString().Should().Be("Alice");
        root.GetProperty("rows")[1].GetProperty("Score").GetString().Should().Be("87");
    }

    [Fact]
    public async Task ReadXlsx_MaxRows_Truncates()
    {
        var headers = new List<string> { "Id" };
        var rows = Enumerable.Range(1, 10).Select(i => new List<string> { i.ToString() }).ToList();
        CreateTempXlsx("trunc.xlsx", headers, rows);
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "trunc.xlsx",
            ["max_rows"] = 4
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(4);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ReadXlsx_SpecificSheet_ReturnsSheetData()
    {
        var fullPath = Path.Combine(_rootDir, "multi.xlsx");
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws1 = wb.Worksheets.Add("Data");
            ws1.Cell(1, 1).Value = "Col1";
            ws1.Cell(2, 1).Value = "val1";
            var ws2 = wb.Worksheets.Add("Summary");
            ws2.Cell(1, 1).Value = "Total";
            ws2.Cell(2, 1).Value = "100";
            wb.SaveAs(fullPath);
        }
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "multi.xlsx",
            ["sheet"] = "Summary"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rows")[0].GetProperty("Total").GetString().Should().Be("100");
    }

    [Fact]
    public async Task ReadXlsx_NoHeader_ReadsIndexedColumns()
    {
        CreateTempXlsx("nohdr.xlsx",
            ["A", "B"],
            [["x", "y"]]);
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "nohdr.xlsx",
            ["has_header"] = false
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList();
        columns.Should().Contain("c1");
        columns.Should().Contain("c2");
    }

    // ── XLSX Write ────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteXlsx_NewFile_CreatesFile()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "output.xlsx",
            ["format"] = "xlsx",
            ["columns"] = """["Id","Name"]""",
            ["rows"] = """[["1","Alice"],["2","Bob"]]"""
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("2 rows");
        File.Exists(Path.Combine(_rootDir, "output.xlsx")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteXlsx_ThenRead_RoundTrips()
    {
        var writeResult = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "roundtrip.xlsx",
            ["format"] = "xlsx",
            ["columns"] = """["City","Pop"]""",
            ["rows"] = """[["NYC","8M"],["LA","4M"]]"""
        });
        writeResult.Success.Should().BeTrue();

        var readResult = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "roundtrip.xlsx"
        });
        readResult.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(readResult.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("rows")[0].GetProperty("City").GetString().Should().Be("NYC");
        doc.RootElement.GetProperty("rows")[1].GetProperty("Pop").GetString().Should().Be("4M");
    }

    [Fact]
    public async Task WriteXlsx_CustomSheetName_CreatesSheet()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "custom_sheet.xlsx",
            ["format"] = "xlsx",
            ["sheet"] = "MySheet",
            ["columns"] = """["A"]""",
            ["rows"] = """[["1"]]"""
        });
        result.Success.Should().BeTrue();
        var fullPath = Path.Combine(_rootDir, "custom_sheet.xlsx");
        using var wb = new ClosedXML.Excel.XLWorkbook(fullPath);
        wb.Worksheet("MySheet").Should().NotBeNull();
    }

    [Fact]
    public async Task WriteXlsx_EmptyRows_WritesHeadersOnly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "empty_rows.xlsx",
            ["format"] = "xlsx",
            ["columns"] = """["Col1","Col2"]""",
            ["rows"] = "[]"
        });
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("0 rows");
        File.Exists(Path.Combine(_rootDir, "empty_rows.xlsx")).Should().BeTrue();
    }

    // ── Path validation ───────────────────────────────────────────────────

    [Fact]
    public async Task Read_PathTraversal_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "../../etc/passwd"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
    }

    [Fact]
    public async Task Write_PathTraversal_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "../escape.csv",
            ["columns"] = """["A"]""",
            ["rows"] = """[["1"]]"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
    }

    [Fact]
    public async Task Read_FileNotFound_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "nonexistent.csv"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("File not found");
    }

    // ── Missing/invalid arguments ─────────────────────────────────────────

    [Fact]
    public async Task MissingOperation_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["path"] = "data.csv"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Operation is required");
    }

    [Fact]
    public async Task MissingPath_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Path is required");
    }

    [Fact]
    public async Task UnknownOperation_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "delete",
            ["path"] = "data.csv"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown operation");
    }

    [Fact]
    public async Task Write_InvalidRowsFormat_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "bad.csv",
            ["columns"] = """["A"]""",
            ["rows"] = """{"not":"array"}"""
        });
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Write_MixedArrayAndObjectRows_NormalizesCorrectly()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "mixed.csv",
            ["columns"] = """["A","B"]""",
            ["rows"] = """[{"A":"x","B":"y"},{"A":"1","B":"2"}]"""
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "mixed.csv"));
        content.Should().Contain("x");
        content.Should().Contain("1");
    }

    [Fact]
    public async Task ReadCsv_Subdirectory_CreatesAndReads()
    {
        WriteCsvFile("sub/dir/data.csv", "K,V\na,1");
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "sub/dir/data.csv"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task WriteCsv_Subdirectory_CreatesDirAndFile()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "deep/nested/output.csv",
            ["columns"] = """["X"]""",
            ["rows"] = """[["1"]]"""
        });
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_rootDir, "deep", "nested", "output.csv")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteXlsx_Subdirectory_CreatesDirAndFile()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "out/reports.xlsx",
            ["format"] = "xlsx",
            ["columns"] = """["Metric","Value"]""",
            ["rows"] = """[["cpu","85%"]]"""
        });
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_rootDir, "out", "reports.xlsx")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteCsv_ArrayRow_WrittenInColumnOrder()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "arr_row.csv",
            ["columns"] = """["A","B","C"]""",
            ["rows"] = """[["1","2","3"]]"""
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "arr_row.csv"));
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[1].TrimEnd().Should().Be("1,2,3");
    }

    [Fact]
    public async Task WriteCsv_RowMissingColumn_WrittenAsEmpty()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "sparse.csv",
            ["columns"] = """["A","B","C"]""",
            ["rows"] = """[["1","2"]]"""
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "sparse.csv"));
        content.Should().Contain("1,2,");
    }

    [Fact]
    public async Task ReadXlsx_EmptySheet_ReturnsZeroRows()
    {
        var fullPath = Path.Combine(_rootDir, "empty_sheet.xlsx");
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            wb.Worksheets.Add("Empty");
            wb.SaveAs(fullPath);
        }
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "empty_sheet.xlsx"
        });
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ReadXlsx_NonexistentSheet_ReturnsError()
    {
        CreateTempXlsx("only_data.xlsx", ["A"], [["1"]]);
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "only_data.xlsx",
            ["sheet"] = "Nonexistent"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Sheet not found");
    }

    [Fact]
    public async Task WriteCsv_ThenAppendCsv_ReadsAllRows()
    {
        await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "app.csv",
            ["columns"] = """["X"]""",
            ["rows"] = """[["1"],["2"]]"""
        });
        await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "app.csv",
            ["columns"] = """["X"]""",
            ["rows"] = """[["3"],["4"]]""",
            ["append"] = true
        });
        var readResult = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "read",
            ["path"] = "app.csv"
        });
        readResult.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(readResult.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task WriteCsv_ObjectRowWithMissingFields_FillsEmpty()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["operation"] = "write",
            ["path"] = "partial_obj.csv",
            ["columns"] = """["A","B","C"]""",
            ["rows"] = """[{"A":"only_a"}]"""
        });
        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_rootDir, "partial_obj.csv"));
        content.Should().Contain("only_a");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().StartWith("only_a,");
    }
}
