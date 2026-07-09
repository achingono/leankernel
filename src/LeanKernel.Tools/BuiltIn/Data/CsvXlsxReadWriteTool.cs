using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.Data;

/// <summary>
/// Provides functionality for csv xlsx read write tool.
/// </summary>
public static class CsvXlsxReadWriteTool
{
    private const string ToolName = "csv_xlsx_read_write";
    private const int DefaultMaxRows = 200;
    private const int MaxRowsLimit = 5000;
    private const string DefaultSheetName = "Sheet1";

    /// <summary>
    /// Executes create.
    /// </summary>
    /// <param name="scopeFactory">The scope factory.</param>
    /// <returns>The operation result.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Read and write CSV/XLSX files in the allowed data directory",
            Category = "data",
            Parameters =
            [
                new ToolParameter { Name = "operation", Type = "string", Description = "Operation: read or write", Required = true },
                new ToolParameter { Name = "path", Type = "string", Description = "Relative file path", Required = true },
                new ToolParameter { Name = "format", Type = "string", Description = "Optional format override: csv or xlsx", Required = false },
                new ToolParameter { Name = "sheet", Type = "string", Description = "XLSX sheet name (read/write)", Required = false },
                new ToolParameter { Name = "has_header", Type = "boolean", Description = "Whether data includes header row (default true)", Required = false },
                new ToolParameter { Name = "max_rows", Type = "integer", Description = "Maximum rows to read (default 200)", Required = false },
                new ToolParameter { Name = "rows", Type = "array", Description = "Rows for write operation (array of objects or arrays)", Required = false },
                new ToolParameter { Name = "append", Type = "boolean", Description = "Append to CSV instead of overwrite", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                using var scope = scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<IOptions<LeanKernelConfig>>().Value.FileSystem;
                var resolved = ParseCsvXlsxArgs(args, config);
                if (resolved.ErrorMessage is not null)
                {
                    return Error(resolved.ErrorMessage);
                }

                try
                {
                    return resolved.Operation switch
                    {
                        "read" => await HandleReadAsync(resolved.FullPath, resolved.RelativePath, resolved.Format, resolved.Sheet, resolved.HasHeader, resolved.RowLimit, ct),
                        "write" => await HandleWriteAsync(args, resolved.FullPath, resolved.RelativePath, resolved.Format, resolved.Sheet, resolved.HasHeader, resolved.Append, ct),
                        _ => Error("Operation must be either 'read' or 'write'")
                    };
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Error($"Permission denied for path '{resolved.RelativePath}': {ex.Message}");
                }
                catch (IOException ex)
                {
                    return Error($"I/O error for path '{resolved.RelativePath}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Error($"CSV/XLSX operation failed: {ex.Message}");
                }
            }
        };
    }

    private sealed record CsvXlsxArgs(
        string Operation,
        string FullPath,
        string RelativePath,
        string Format,
        string? Sheet,
        bool HasHeader,
        int RowLimit,
        bool Append,
        string? ErrorMessage);

    private static CsvXlsxArgs ParseCsvXlsxArgs(IDictionary<string, object?> args, LeanKernel.Abstractions.Configuration.FileSystemConfig config)
    {
        var operation = ToolArgumentReader.GetString(args, "operation").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(operation))
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Operation is required");
        }

        if (operation is not "read" and not "write")
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Operation must be either 'read' or 'write'");
        }

        var relativePath = ToolArgumentReader.GetString(args, "path");
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Path is required");
        }

        var fullPath = FileSystemSupport.ResolveWithinRoot(config.AllowedRoot, relativePath);
        if (fullPath is null)
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Access denied: path is outside the allowed directory");
        }

        var formatArg = ToolArgumentReader.GetString(args, "format");
        var format = ResolveFormat(formatArg, relativePath);
        if (format is null)
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Unable to infer format from path. Specify format as 'csv' or 'xlsx'");
        }

        if (format is not "csv" and not "xlsx")
        {
            return new CsvXlsxArgs(operation, null!, null!, null!, null, false, 0, false, "Unsupported format. Use 'csv' or 'xlsx'");
        }

        var sheet = ToolArgumentReader.GetString(args, "sheet");
        var hasHeader = ToolArgumentReader.GetBoolOrDefault(args, "has_header", true);
        var maxRows = ClampMaxRows(ToolArgumentReader.GetInt32OrDefault(args, "max_rows", DefaultMaxRows));
        var append = ToolArgumentReader.GetBoolOrDefault(args, "append", false);

        return new CsvXlsxArgs(operation, fullPath, relativePath, format, sheet, hasHeader, maxRows, append, null);
    }

    private static async Task<ToolResult> HandleReadAsync(
        string fullPath,
        string relativePath,
        string format,
        string? sheet,
        bool hasHeader,
        int maxRows,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath))
        {
            return Error($"File not found: {relativePath}");
        }

        return format == "csv"
            ? await ReadCsvAsync(fullPath, maxRows, hasHeader, ct)
            : ReadXlsx(fullPath, maxRows, hasHeader, sheet);
    }

    private static async Task<ToolResult> ReadCsvAsync(string fullPath, int maxRows, bool hasHeader, CancellationToken ct)
    {
        await using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            Delimiter = ",",
            TrimOptions = TrimOptions.None
        };

        using var csv = new CsvReader(reader, csvConfig);

        return hasHeader
            ? await ReadCsvWithHeaderAsync(csv, maxRows, ct)
            : await ReadCsvWithoutHeaderAsync(csv, maxRows, ct);
    }

    private static async Task<ToolResult> ReadCsvWithHeaderAsync(CsvReader csv, int maxRows, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;

        if (!await csv.ReadAsync())
        {
            return SuccessPayload(Array.Empty<string>(), rows, 0, false);
        }

        csv.ReadHeader();
        var columns = csv.HeaderRecord?.ToList() ?? [];

        while (await csv.ReadAsync())
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            ct.ThrowIfCancellationRequested();
            rows.Add(ReadCsvRow(csv, columns.Count));
        }

        return SuccessPayload(columns, rows, rows.Count, truncated);
    }

    private static async Task<ToolResult> ReadCsvWithoutHeaderAsync(CsvReader csv, int maxRows, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<string>>();
        var maxColumns = 0;
        var truncated = false;

        while (await csv.ReadAsync())
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            ct.ThrowIfCancellationRequested();
            var record = csv.Parser.Record ?? [];
            maxColumns = Math.Max(maxColumns, record.Length);
            rows.Add(record.Select(value => value ?? string.Empty).ToArray());
        }

        var columns = Enumerable.Range(1, maxColumns).Select(index => $"c{index}").ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i] = PadRow(rows[i], maxColumns);
        }

        return SuccessPayload(columns, rows, rows.Count, truncated);
    }

    private static ToolResult ReadXlsx(string fullPath, int maxRows, bool hasHeader, string? sheet)
    {
        using var workbook = new XLWorkbook(fullPath);
        var worksheet = ResolveWorksheet(workbook, sheet, out var sheetError);
        if (worksheet is null)
        {
            return Error(sheetError ?? "Unable to resolve worksheet.");
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return SuccessPayload(Array.Empty<string>(), [], 0, false);
        }

        var firstRow = usedRange.FirstRow().RowNumber();
        var lastRow = usedRange.LastRow().RowNumber();
        var firstColumn = usedRange.FirstColumn().ColumnNumber();
        var lastColumn = usedRange.LastColumn().ColumnNumber();

        List<string> columns;
        var dataStartRow = firstRow;
        if (hasHeader)
        {
            columns = new List<string>(lastColumn - firstColumn + 1);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var name = worksheet.Cell(firstRow, column).GetFormattedString();
                columns.Add(string.IsNullOrWhiteSpace(name) ? $"c{column - firstColumn + 1}" : name);
            }

            dataStartRow = firstRow + 1;
        }
        else
        {
            columns = Enumerable.Range(1, lastColumn - firstColumn + 1).Select(index => $"c{index}").ToList();
        }

        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;

        for (var rowIndex = dataStartRow; rowIndex <= lastRow; rowIndex++)
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            var row = new string[lastColumn - firstColumn + 1];
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                row[column - firstColumn] = worksheet.Cell(rowIndex, column).GetFormattedString();
            }

            rows.Add(row);
        }

        return SuccessPayload(columns, rows, rows.Count, truncated);
    }

    private static async Task<ToolResult> HandleWriteAsync(
        IDictionary<string, object?> args,
        string fullPath,
        string relativePath,
        string format,
        string? sheet,
        bool hasHeader,
        bool append,
        CancellationToken ct)
    {
        var rowsElement = GetRowsElement(args);
        if (rowsElement is null)
        {
            return Error("Rows are required for write operation");
        }

        if (rowsElement.Value.ValueKind != JsonValueKind.Array)
        {
            return Error("Rows must be an array");
        }

        var normalized = NormalizeRows(rowsElement.Value);
        if (!normalized.Success)
        {
            return Error(normalized.Error ?? "Rows are invalid.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (format == "xlsx" && append)
        {
            return Error("Append is only supported for CSV");
        }

        if (format == "csv")
        {
            await WriteCsvAsync(fullPath, normalized.Columns, normalized.Rows, hasHeader, append, ct);
        }
        else
        {
            WriteXlsx(fullPath, normalized.Columns, normalized.Rows, hasHeader, string.IsNullOrWhiteSpace(sheet) ? DefaultSheetName : sheet);
        }

        return new ToolResult
        {
            ToolName = ToolName,
            Success = true,
            Output = $"Wrote {normalized.Rows.Count} rows to {relativePath}"
        };
    }

    private static async Task WriteCsvAsync(
        string fullPath,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool hasHeader,
        bool append,
        CancellationToken ct)
    {
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = false,
            TrimOptions = TrimOptions.None
        };

        var shouldWriteHeader = hasHeader && (!append || !File.Exists(fullPath) || new FileInfo(fullPath).Length == 0);
        await using var stream = new FileStream(fullPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var csv = new CsvWriter(writer, csvConfig);

        if (shouldWriteHeader && columns.Count > 0)
        {
            foreach (var column in columns)
            {
                csv.WriteField(column);
            }

            await csv.NextRecordAsync();
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var value in row)
            {
                csv.WriteField(value);
            }

            await csv.NextRecordAsync();
        }

        await writer.FlushAsync(ct);
    }

    private static void WriteXlsx(
        string fullPath,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool hasHeader,
        string sheetName)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        var rowOffset = 1;
        if (hasHeader && columns.Count > 0)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                worksheet.Cell(1, columnIndex + 1).Value = columns[columnIndex];
            }

            rowOffset = 2;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                worksheet.Cell(rowIndex + rowOffset, columnIndex + 1).Value = row[columnIndex];
            }
        }

        workbook.SaveAs(fullPath);
    }

    private static (bool Success, string? Error, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) NormalizeRows(JsonElement rowsElement)
    {
        if (rowsElement.GetArrayLength() == 0)
        {
            return (true, null, Array.Empty<string>(), []);
        }

        var first = rowsElement.EnumerateArray().First();
        if (first.ValueKind == JsonValueKind.Object)
        {
            return NormalizeObjectRows(rowsElement);
        }

        if (first.ValueKind == JsonValueKind.Array)
        {
            return NormalizeArrayRows(rowsElement);
        }

        return (false, "Rows must contain objects or arrays", Array.Empty<string>(), []);
    }

    private static (bool Success, string? Error, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) NormalizeObjectRows(JsonElement rowsElement)
    {
        var rowObjects = new List<Dictionary<string, string>>();
        var keySet = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var element in rowsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return (false, "Rows must be all objects or all arrays; mixed row types are not supported", Array.Empty<string>(), []);
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                row[property.Name] = NormalizeCellValue(property.Value);
                keySet.Add(property.Name);
            }

            rowObjects.Add(row);
        }

        var columns = keySet.ToList();
        var rows = rowObjects
            .Select(row => (IReadOnlyList<string>)columns.Select(column => row.TryGetValue(column, out var value) ? value : string.Empty).ToArray())
            .ToList();
        return (true, null, columns, rows);
    }

    private static (bool Success, string? Error, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) NormalizeArrayRows(JsonElement rowsElement)
    {
        var rows = new List<IReadOnlyList<string>>();
        var maxColumns = 0;

        foreach (var element in rowsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return (false, "Rows must be all objects or all arrays; mixed row types are not supported", Array.Empty<string>(), []);
            }

            var row = element.EnumerateArray().Select(NormalizeCellValue).ToArray();
            maxColumns = Math.Max(maxColumns, row.Length);
            rows.Add(row);
        }

        var columns = Enumerable.Range(1, maxColumns).Select(index => $"c{index}").ToList();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rows[rowIndex] = PadRow(rows[rowIndex], maxColumns);
        }

        return (true, null, columns, rows);
    }

    private static IReadOnlyList<string> ReadCsvRow(CsvReader csv, int columnCount)
    {
        var row = new string[columnCount];
        for (var index = 0; index < columnCount; index++)
        {
            row[index] = csv.TryGetField(index, out string? value) ? value ?? string.Empty : string.Empty;
        }

        return row;
    }

    private static IReadOnlyList<string> PadRow(IReadOnlyList<string> row, int width)
    {
        if (row.Count == width)
        {
            return row;
        }

        var padded = new string[width];
        for (var index = 0; index < width; index++)
        {
            padded[index] = index < row.Count ? row[index] : string.Empty;
        }

        return padded;
    }

    private static IXLWorksheet? ResolveWorksheet(XLWorkbook workbook, string? sheet, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(sheet))
        {
            return workbook.Worksheets.FirstOrDefault();
        }

        var worksheet = workbook.Worksheets.FirstOrDefault(item => string.Equals(item.Name, sheet, StringComparison.Ordinal));
        if (worksheet is not null)
        {
            return worksheet;
        }

        var available = string.Join(", ", workbook.Worksheets.Select(item => item.Name));
        error = $"Sheet '{sheet}' was not found. Available sheets: {available}";
        return null;
    }

    private static JsonElement? GetRowsElement(IDictionary<string, object?> args)
    {
        if (!args.TryGetValue("rows", out var rows) || rows is null)
        {
            return null;
        }

        return rows switch
        {
            JsonElement element => element,
            string json when !string.IsNullOrWhiteSpace(json) => JsonDocument.Parse(json).RootElement.Clone(),
            _ => JsonSerializer.SerializeToElement(rows)
        };
    }

    private static string NormalizeCellValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => element.GetRawText()
        };
    }

    private static string? ResolveFormat(string formatArg, string path)
    {
        var normalizedFormat = formatArg.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedFormat))
        {
            return normalizedFormat;
        }

        var extension = Path.GetExtension(path).Trim().ToLowerInvariant();
        return extension switch
        {
            ".csv" => "csv",
            ".xlsx" => "xlsx",
            _ => null
        };
    }

    private static int ClampMaxRows(int value)
    {
        if (value <= 0)
        {
            return DefaultMaxRows;
        }

        return Math.Min(value, MaxRowsLimit);
    }

    private static ToolResult SuccessPayload(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, int rowCount, bool truncated)
    {
        return new ToolResult
        {
            ToolName = ToolName,
            Success = true,
            Output = JsonSerializer.Serialize(new
            {
                columns,
                rows,
                rowCount,
                truncated
            })
        };
    }

    private static ToolResult Error(string message)
    {
        return new ToolResult
        {
            ToolName = ToolName,
            Success = false,
            Error = message
        };
    }
}
