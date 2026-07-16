using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn.Data;

/// <summary>
/// Reads and writes CSV and XLSX files.
/// </summary>
public static class CsvXlsxReadWriteTool
{
    private const string ToolName = "csv_xlsx_read_write";
    private const int DefaultMaxRows = 200;
    private const int MaxRowsLimit = 5000;
    private const string DefaultSheetName = "Sheet1";

    [SuppressMessage("Critical Code Smell", "S3776", Justification = "Tool handler remains explicit to preserve operation/path validation and format branching.")]
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Read and write CSV and XLSX files within the allowed data directory",
            Category = "data",
            Parameters =
            [
                new ToolParameter { Name = "operation", Type = "string", Description = "Operation: read or write", Required = true },
                new ToolParameter { Name = "path", Type = "string", Description = "Relative file path", Required = true },
                new ToolParameter { Name = "format", Type = "string", Description = "File format: csv or xlsx (auto-detected)", Required = false },
                new ToolParameter { Name = "sheet", Type = "string", Description = "Sheet name for XLSX (default: Sheet1)", Required = false },
                new ToolParameter { Name = "has_header", Type = "boolean", Description = "Whether CSV has a header row (default: true)", Required = false },
                new ToolParameter { Name = "max_rows", Type = "integer", Description = "Maximum rows to read (default: 200)", Required = false },
                new ToolParameter { Name = "append", Type = "boolean", Description = "Append to existing CSV file", Required = false },
                new ToolParameter { Name = "columns", Type = "object", Description = "Array of column names for write", Required = false },
                new ToolParameter { Name = "rows", Type = "object", Description = "Array of row objects or arrays for write", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var operation = ToolArgumentReader.GetString(args, "operation");
                var path = ToolArgumentReader.GetString(args, "path");
                if (string.IsNullOrWhiteSpace(operation)) return Err("Operation is required (read or write)");
                if (string.IsNullOrWhiteSpace(path)) return Err("Path is required");

                using var scope = scopeFactory.CreateScope();
                var fileSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileSettings>>().Value;
                var fullPath = FileSystemSupport.ResolveWithinRoot(fileSettings.RootPath, path);
                if (fullPath is null) return Err("Access denied: path is outside the allowed directory");

                var format = ToolArgumentReader.GetString(args, "format");
                if (string.IsNullOrWhiteSpace(format))
                    format = Path.GetExtension(fullPath).ToLowerInvariant() is ".xlsx" or ".xlsm" ? "xlsx" : "csv";

                if (string.Equals(operation, "read", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(fullPath)) return Err($"File not found: {path}");
                    return format == "xlsx" ? ReadXlsx(fullPath, args) : await ReadCsvAsync(fullPath, args);
                }

                if (string.Equals(operation, "write", StringComparison.OrdinalIgnoreCase))
                {
                    var columnsRaw = ToolArgumentReader.GetJson(args, "columns");
                    var rowsRaw = ToolArgumentReader.GetJson(args, "rows");
                    if (string.IsNullOrWhiteSpace(columnsRaw) || string.IsNullOrWhiteSpace(rowsRaw))
                        return Err("Columns and rows are required for write operation");

                    var columns = JsonSerializer.Deserialize<List<string>>(columnsRaw) ?? [];
                    var rowsElement = JsonDocument.Parse(rowsRaw);
                    var (normOk, normError, normColumns, normRows) = NormalizeRows(rowsElement.RootElement);
                    if (!normOk) return Err(normError!);

                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    var append = ToolArgumentReader.GetBoolOrDefault(args, "append", false);
                    var sheet = ToolArgumentReader.GetString(args, "sheet") ?? DefaultSheetName;
                    var useColumns = columns.Count > 0 ? columns : normColumns!;

                    return format == "xlsx"
                        ? WriteXlsx(fullPath, useColumns, normRows!, sheet)
                        : await WriteCsvAsync(fullPath, useColumns, normRows!, append);
                }

                return Err($"Unknown operation '{operation}'. Use 'read' or 'write'.");
            }
        };
    }

    [SuppressMessage("Critical Code Smell", "S3776", Justification = "CSV read logic keeps header/no-header branches explicit for predictable output shape.")]
    private static async Task<ToolResult> ReadCsvAsync(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var maxRows = Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "max_rows", DefaultMaxRows), 1, MaxRowsLimit);
        var hasHeader = ToolArgumentReader.GetBoolOrDefault(args, "has_header", true);

        using var reader = new StreamReader(fullPath);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            MissingFieldFound = null
        });

        var records = new List<Dictionary<string, string>>();
        var rowCount = 0;

        if (hasHeader)
        {
            if (!await csv.ReadAsync())
                return Ok(JsonSerializer.Serialize(new { columns = new List<string>(), rows = new List<Dictionary<string, string>>(), rowCount = 0, truncated = false }));
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? [];
            while (await csv.ReadAsync() && rowCount < maxRows)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in headers) row[header] = csv.GetField(header) ?? string.Empty;
                records.Add(row);
                rowCount++;
            }
        }
        else
        {
            while (await csv.ReadAsync() && rowCount < maxRows)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < 100; i++)
                {
                    var val = csv.GetField(i);
                    if (val is null) break;
                    row[$"c{i + 1}"] = val;
                }
                records.Add(row);
                rowCount++;
            }
        }

        var truncated = rowCount >= maxRows && await csv.ReadAsync();
        return Ok(JsonSerializer.Serialize(new { columns = records.FirstOrDefault()?.Keys.ToList() ?? [], rows = records, rowCount = records.Count, truncated }));
    }

    private static ToolResult ReadXlsx(string fullPath, IReadOnlyDictionary<string, object?> args)
    {
        var maxRows = Math.Clamp(ToolArgumentReader.GetInt32OrDefault(args, "max_rows", DefaultMaxRows), 1, MaxRowsLimit);
        var hasHeader = ToolArgumentReader.GetBoolOrDefault(args, "has_header", true);
        var sheetName = ToolArgumentReader.GetString(args, "sheet");

        using var workbook = new XLWorkbook(fullPath);
        IXLWorksheet? ws = null;
        try
        {
            ws = string.IsNullOrWhiteSpace(sheetName) ? workbook.Worksheet(1) : workbook.Worksheet(sheetName);
        }
        catch (ArgumentException)
        {
            return Err($"Sheet not found: {sheetName}");
        }
        if (ws is null) return Err($"Sheet not found: {sheetName}");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        List<string> columns;
        int startRow;
        if (hasHeader)
        {
            columns = Enumerable.Range(1, lastCol).Select(c => ws.Cell(1, c).GetString()).ToList();
            startRow = 2;
        }
        else
        {
            columns = Enumerable.Range(1, lastCol).Select(c => $"c{c}").ToList();
            startRow = 1;
        }

        var rows = new List<Dictionary<string, string>>();
        var rowCount = 0;
        for (var r = startRow; r <= lastRow && rowCount < maxRows; r++)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < columns.Count; c++)
            {
                row[columns[c]] = ws.Cell(r, c + 1).GetString();
            }
            rows.Add(row);
            rowCount++;
        }

        var truncated = rowCount >= maxRows && lastRow > startRow + rowCount - 1;
        return Ok(JsonSerializer.Serialize(new { columns, rows, rowCount = rows.Count, truncated }));
    }

    private static async Task<ToolResult> WriteCsvAsync(string fullPath, List<string> columns, List<List<string>> rows, bool append)
    {
        await using var writer = new StreamWriter(fullPath, append, new UTF8Encoding(false));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true });

        if (append && File.Exists(fullPath) && new FileInfo(fullPath).Length > 0)
        {
            // Skip header for append
        }
        else
        {
            foreach (var col in columns) csv.WriteField(col);
            csv.NextRecord();
        }

        foreach (var row in rows)
        {
            foreach (var col in columns)
            {
                var idx = columns.IndexOf(col);
                csv.WriteField(idx < row.Count ? row[idx] : string.Empty);
            }
            csv.NextRecord();
        }

        return Ok($"Wrote {rows.Count} rows to {Path.GetFileName(fullPath)}");
    }

    private static ToolResult WriteXlsx(string fullPath, List<string> columns, List<List<string>> rows, string sheetName)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);

        for (var c = 0; c < columns.Count; c++) ws.Cell(1, c + 1).Value = columns[c];

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var idx = c;
                ws.Cell(r + 2, c + 1).Value = idx < rows[r].Count ? rows[r][idx] : string.Empty;
            }
        }

        if (columns.Count > 0) ws.Range(1, 1, 1, columns.Count).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        workbook.SaveAs(fullPath);
        return Ok($"Wrote {rows.Count} rows to {Path.GetFileName(fullPath)}");
    }

    [SuppressMessage("Critical Code Smell", "S3776", Justification = "Row normalization intentionally supports both object and array row contracts.")]
    private static (bool Success, string? Error, List<string>? Columns, List<List<string>>? Rows) NormalizeRows(JsonElement rowsElement)
    {
        if (rowsElement.ValueKind != JsonValueKind.Array) return (false, "Rows must be a JSON array", null, null);

        var result = new List<List<string>>();
        List<string>? columns = null;

        foreach (var item in rowsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (columns is null) columns = [.. item.EnumerateObject().Select(p => p.Name)];
                var row = new List<string>();
                foreach (var col in columns!)
                {
                    if (item.TryGetProperty(col, out var val))
                        row.Add(val.ValueKind == JsonValueKind.Null ? string.Empty : val.ToString());
                    else
                        row.Add(string.Empty);
                }
                result.Add(row);
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                var row = item.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? string.Empty : v.ToString()).ToList();
                if (columns is null) columns = Enumerable.Range(1, row.Count).Select(i => $"c{i}").ToList();
                result.Add(row);
            }
            else
            {
                return (false, "Each row must be a JSON object or array", null, null);
            }
        }

        columns ??= [];
        return (true, null, columns, result);
    }

    private static ToolResult Ok(string output) => new() { ToolName = ToolName, Success = true, Output = output };
    private static ToolResult Err(string error) => new() { ToolName = ToolName, Success = false, Error = error };
}
