using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LeanKernel.Logic.Tools.BuiltIn.Data;

/// <summary>
/// Executes parameterized read-only SQL queries against configured database connections.
/// </summary>
public static partial class DatabaseQueryTool
{
    private const string ToolName = "database_query";
    private static readonly string[] BlockedKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "ALTER", "DROP", "TRUNCATE",
        "CREATE", "GRANT", "COPY", "DO", "CALL", "EXECUTE"
    ];

    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Execute a parameterized SQL query against a configured read-only database connection",
            Category = "data",
            Parameters =
            [
                new ToolParameter { Name = "connection", Type = "string", Description = "Configured connection name", Required = true },
                new ToolParameter { Name = "query", Type = "string", Description = "SQL query to execute", Required = true },
                new ToolParameter { Name = "parameters", Type = "object", Description = "Parameter map for query placeholders", Required = false },
                new ToolParameter { Name = "max_rows", Type = "integer", Description = "Maximum rows to return", Required = false },
                new ToolParameter { Name = "timeout_seconds", Type = "integer", Description = "Command timeout in seconds", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                try
                {
                    return await ExecuteQueryAsync(args, scopeFactory, ct);
                }
                catch (ArgumentException ex)
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = ex.Message };
                }
                catch (DbException ex)
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = $"Database query failed: {ex.Message}" };
                }
                catch (Exception ex)
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = $"Database query execution failed: {ex.Message}" };
                }
            }
        };
    }

    private static async Task<ToolResult> ExecuteQueryAsync(
        IReadOnlyDictionary<string, object?> args, IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var connectionName = ToolArgumentReader.GetString(args, "connection");
        var query = ToolArgumentReader.GetString(args, "query");
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = "Connection is required" };
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = "Query is required" };
        }

        var parameters = ToolArgumentReader.GetObjectDictionary(args, "parameters");

        using var scope = scopeFactory.CreateScope();
        var dbSettings = scope.ServiceProvider.GetRequiredService<IOptions<AgentSettings>>().Value.Tools.DatabaseQuery;
        var connectionConfig = dbSettings.Connections.FirstOrDefault(
            entry => string.Equals(entry.Name, connectionName, StringComparison.OrdinalIgnoreCase));

        if (connectionConfig is null)
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = $"Unknown database connection: {connectionName}" };
        }
        if (!connectionConfig.ReadOnly)
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = $"Database connection '{connectionConfig.Name}' must be configured with readOnly=true" };
        }

        var provider = connectionConfig.Provider.Trim().ToLowerInvariant();
        if (provider is not ("postgres" or "sqlite"))
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = $"Unsupported database provider '{connectionConfig.Provider}'. Supported: postgres, sqlite" };
        }

        var configuredMaxRows = Math.Max(1, dbSettings.MaxRows);
        var requestedMaxRows = ToolArgumentReader.GetInt32OrDefault(args, "max_rows", configuredMaxRows);
        var configuredTimeout = Math.Max(1, dbSettings.DefaultTimeoutSeconds);
        var requestedTimeout = ToolArgumentReader.GetInt32OrDefault(args, "timeout_seconds", configuredTimeout);
        var maxRows = Math.Clamp(requestedMaxRows, 1, configuredMaxRows);
        var timeoutSeconds = Math.Clamp(requestedTimeout, 1, configuredTimeout);

        if (!TryValidateReadOnlyQuery(query, provider, connectionConfig.AllowedSchemas, out var validationError))
        {
            return new ToolResult { ToolName = ToolName, Success = false, Error = validationError };
        }

        await using var connection = CreateConnection(connectionConfig, provider);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = timeoutSeconds;
        AddParameters(command, parameters);

        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<List<object?>>();
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }
            var row = new List<object?>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row.Add(NormalizeResultValue(reader.GetValue(index)));
            }
            rows.Add(row);
        }
        stopwatch.Stop();

        var output = JsonSerializer.Serialize(new
        {
            columns, rows, rowCount = rows.Count, truncated, executionMs = stopwatch.ElapsedMilliseconds
        });

        return new ToolResult { ToolName = ToolName, Success = true, Output = output };
    }

    private static DbConnection CreateConnection(DatabaseQueryConnectionSettings connectionConfig, string provider)
    {
        return provider switch
        {
            "postgres" => new NpgsqlConnection(connectionConfig.ConnectionString),
            "sqlite" => new SqliteConnection(connectionConfig.ConnectionString),
            _ => throw new ArgumentException($"Unsupported provider '{connectionConfig.Provider}'.")
        };
    }

    private static void AddParameters(DbCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var pair in parameters)
        {
            var parameterName = pair.Key.Trim().TrimStart('@', ':', '$', '?');
            if (string.IsNullOrWhiteSpace(parameterName) || !SafeParameterNameRegex().IsMatch(parameterName))
            {
                throw new ArgumentException($"Invalid parameter name '{pair.Key}'.");
            }
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@{parameterName}";
            parameter.Value = NormalizeParameterValue(pair.Value);
            command.Parameters.Add(parameter);
        }
    }

    private static object NormalizeParameterValue(object? value)
    {
        if (value is null) return DBNull.Value;
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => DBNull.Value,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => GetNumericValue(element),
                _ => element.GetRawText()
            };
        }
        return value;
    }

    private static object GetNumericValue(JsonElement element)
    {
        if (element.TryGetInt64(out var integerValue))
        {
            return integerValue;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return element.GetDouble();
    }

    private static object? NormalizeResultValue(object value)
    {
        if (value is DBNull) return null;
        return value switch
        {
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }

    private static bool TryValidateReadOnlyQuery(string query, string provider, IReadOnlyCollection<string> allowedSchemas, out string? error)
    {
        error = null;
        var stripped = StripSqlLiteralsAndComments(query);
        var normalized = NormalizeWhitespace(stripped).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) { error = "Query is required"; return false; }
        var withoutTrailingSemicolon = normalized.EndsWith(';') ? normalized[..^1].TrimEnd() : normalized;
        if (withoutTrailingSemicolon.Contains(';', StringComparison.Ordinal)) { error = "Only a single SQL statement is allowed"; return false; }
        if (!StartsWithAllowedKeyword(withoutTrailingSemicolon)) { error = "Only SELECT, WITH ... SELECT, or EXPLAIN statements are allowed"; return false; }
        if (ContainsBlockedKeyword(withoutTrailingSemicolon, out var blockedKeyword)) { error = $"Blocked SQL keyword detected: {blockedKeyword}"; return false; }
        if (ContainsCteDml(withoutTrailingSemicolon, out var cteDmlKeyword)) { error = $"Blocked CTE-DML pattern detected: {cteDmlKeyword}"; return false; }
        if (provider == "postgres" && allowedSchemas.Count > 0 && !AreSchemasAllowed(withoutTrailingSemicolon, allowedSchemas, out var schemaError)) { error = schemaError; return false; }
        return true;
    }

    private static bool StartsWithAllowedKeyword(string query)
    {
        return query.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
            || query.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase)
            || query.Equals("WITH", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("EXPLAIN ", StringComparison.OrdinalIgnoreCase)
            || query.Equals("EXPLAIN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsBlockedKeyword(string query, out string? keyword)
    {
        foreach (var blockedKeyword in BlockedKeywords)
        {
            if (Regex.IsMatch(query, $@"\b{blockedKeyword}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                keyword = blockedKeyword;
                return true;
            }
        }
        keyword = null;
        return false;
    }

    private static bool ContainsCteDml(string query, out string? dmlKeyword)
    {
        var match = CteDmlRegex().Match(query);
        if (match.Success) { dmlKeyword = match.Groups["verb"].Value.ToUpperInvariant(); return true; }
        dmlKeyword = null;
        return false;
    }

    private static bool AreSchemasAllowed(string query, IReadOnlyCollection<string> allowedSchemas, out string? error)
    {
        var allowed = new HashSet<string>(allowedSchemas.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeIdentifier), StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) { error = null; return true; }
        var matches = SchemaReferenceRegex().Matches(query);
        foreach (Match match in matches)
        {
            var identifier = match.Groups["identifier"].Value;
            if (string.IsNullOrWhiteSpace(identifier)) continue;
            var parts = identifier.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var schema = NormalizeIdentifier(parts[0]);
            if (!allowed.Contains(schema)) { error = $"Schema '{schema}' is not allowed for this connection"; return false; }
        }
        error = null;
        return true;
    }

    private static string NormalizeIdentifier(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')) || (trimmed.StartsWith('`') && trimmed.EndsWith('`'))))
        {
            trimmed = trimmed[1..^1];
        }
        return trimmed.Trim().ToLowerInvariant();
    }

    private static string NormalizeWhitespace(string value) => MultipleWhitespaceRegex().Replace(value, " ");

    [SuppressMessage("Critical Code Smell", "S3776", Justification = "SQL sanitizer uses an explicit state machine for predictable token stripping.")]
    private static string StripSqlLiteralsAndComments(string sql)
    {
        var builder = new StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment) { if (current == '\n') { inLineComment = false; builder.Append('\n'); } else { builder.Append(' '); } }
            else if (inBlockComment) { if (current == '*' && next == '/') { inBlockComment = false; builder.Append("  "); index++; } else { builder.Append(char.IsWhiteSpace(current) ? current : ' '); } }
            else if (inSingleQuote) { if (current == '\'' && next == '\'') { builder.Append("  "); index++; } else if (current == '\'') { inSingleQuote = false; builder.Append(' '); } else { builder.Append(char.IsWhiteSpace(current) ? current : ' '); } }
            else if (inDoubleQuote) { if (current == '"' && next == '"') { builder.Append("\"\""); index++; } else if (current == '"') { inDoubleQuote = false; builder.Append('"'); } else { builder.Append(current); } }
            else
            {
                if (current == '-' && next == '-') { inLineComment = true; builder.Append("  "); index++; }
                else if (current == '/' && next == '*') { inBlockComment = true; builder.Append("  "); index++; }
                else if (current == '\'') { inSingleQuote = true; builder.Append(' '); }
                else if (current == '"') { inDoubleQuote = true; builder.Append('"'); }
                else { builder.Append(current); }
            }
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SafeParameterNameRegex();

    [GeneratedRegex(@"\bWITH\b[\s\S]*?\bAS\s*\(\s*(?<verb>INSERT|UPDATE|DELETE|MERGE|COPY|DO|CALL|EXECUTE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CteDmlRegex();

    [GeneratedRegex(@"\b(?:FROM|JOIN)\s+(?<identifier>(?:""[^""]+""|\[[^\]]+\]|`[^`]+`|[A-Za-z_][\w$]*)(?:\s*\.\s*(?:""[^""]+""|\[[^\]]+\]|`[^`]+`|[A-Za-z_][\w$]*))?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SchemaReferenceRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MultipleWhitespaceRegex();
}
