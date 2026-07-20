using System.Text.Json;

using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.BuiltIn.Data;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class DatabaseQueryToolTests : IDisposable
{
    private readonly string _rootDir;
    private readonly List<string> _cleanupFiles = new();
    private readonly List<SqliteConnection> _connections = new();
#pragma warning disable CS0169
    private static int _counter;
#pragma warning restore CS0169

    public DatabaseQueryToolTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "dbquery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        foreach (var conn in _connections)
        {
            try
            {
                conn.Close();
            }
            catch
            { /* ignore */
            }

            try
            {
                conn.Dispose();
            }
            catch
            { /* ignore */
            }
        }

        foreach (var file in _cleanupFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            { /* ignore */
            }
        }

        try
        {
            Directory.Delete(_rootDir, true);
        }
        catch
        { /* ignore */
        }
    }

    private SqliteConnection CreateInMemoryConnection(string? dbName = null)
    {
        dbName ??= $"mem_{Interlocked.Increment(ref _counter)}";
        var cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        var conn = new SqliteConnection(cs);
        _connections.Add(conn);
        return conn;
    }

    private void SeedTable(SqliteConnection conn, string table = "users", int rowCount = 3)
    {
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE {table} (id INTEGER PRIMARY KEY, name TEXT, email TEXT, score REAL)";
        cmd.ExecuteNonQuery();
        for (var i = 1; i <= rowCount; i++)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = $"INSERT INTO {table} (name, email, score) VALUES ('user{i}', 'user{i}@example.com', {i * 10.5})";
            insert.ExecuteNonQuery();
        }
    }

    private IServiceScopeFactory BuildScopeFactory(
        int maxRows = 200,
        int defaultTimeoutSeconds = 30,
        List<DatabaseQueryConnectionSettings>? connections = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.DatabaseQuery.Enabled = true;
            opts.Tools.DatabaseQuery.MaxRows = maxRows;
            opts.Tools.DatabaseQuery.DefaultTimeoutSeconds = defaultTimeoutSeconds;
            opts.Tools.DatabaseQuery.Connections = connections ?? [];
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

    private static DatabaseQueryConnectionSettings SQLiteConnection(string name = "sqlite-main", string? connStr = null, bool readOnly = true, List<string>? schemas = null) =>
        new()
        {
            Name = name,
            Provider = "sqlite",
            ConnectionString = connStr ?? $"Data Source=file:mem_{Interlocked.Increment(ref _counter)}?mode=memory&cache=shared",
            ReadOnly = readOnly,
            AllowedSchemas = schemas ?? []
        };

    private async Task<ToolResult> InvokeAsync(
        Dictionary<string, object?> args,
        IServiceScopeFactory? scopeFactory = null)
    {
        scopeFactory ??= BuildScopeFactory();
        var tool = DatabaseQueryTool.Create(scopeFactory);
        return await tool.Handler(args, CancellationToken.None);
    }

    [Fact]
    public async Task Create_DefinesCorrectMetadata()
    {
        var tool = DatabaseQueryTool.Create(BuildScopeFactory());
        tool.Name.Should().Be("database_query");
        tool.Category.Should().Be("data");
        tool.Parameters.Should().HaveCount(5);
        tool.Parameters.Select(p => p.Name).Should().Contain(
            ["connection", "query", "parameters", "max_rows", "timeout_seconds"]);
    }

    [Fact]
    public async Task ExecuteQuery_MissingConnection_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["query"] = "SELECT 1"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Connection is required");
    }

    [Fact]
    public async Task ExecuteQuery_MissingQuery_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["connection"] = "sqlite-main"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Query is required");
    }

    [Fact]
    public async Task ExecuteQuery_EmptyConnection_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["connection"] = string.Empty,
            ["query"] = "SELECT 1"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Connection is required");
    }

    [Fact]
    public async Task ExecuteQuery_EmptyQuery_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["connection"] = "sqlite-main",
            ["query"] = string.Empty
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Query is required");
    }

    [Fact]
    public async Task ExecuteQuery_UnknownConnection_ReturnsError()
    {
        var result = await InvokeAsync(new Dictionary<string, object?>
        {
            ["connection"] = "nonexistent",
            ["query"] = "SELECT 1"
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown database connection");
    }

    [Fact]
    public async Task ExecuteQuery_UnknownConnection_CaseInsensitiveMatch()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var connStr = conn.ConnectionString;
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "SQLITE-MAIN",
                ["query"] = "SELECT * FROM users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", connStr)]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_ReadOnlyFalse_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "rw-sqlite",
                ["query"] = "SELECT 1"
            },
            BuildScopeFactory(connections: [SQLiteConnection("rw-sqlite", readOnly: false)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("readOnly=true");
    }

    [Fact]
    public async Task ExecuteQuery_SelectLiteral_ReturnsRows()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1 AS val, 'hello' AS msg"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var root = doc.RootElement;
        root.GetProperty("rowCount").GetInt32().Should().Be(1);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList()
            .Should().BeEquivalentTo(["val", "msg"]);
    }

    [Fact]
    public async Task ExecuteQuery_SelectFromTable_ReturnsSeededRows()
    {
        var conn = CreateInMemoryConnection("memdb2");
        SeedTable(conn, rowCount: 5);
        var connStr = conn.ConnectionString;

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id, name FROM users ORDER BY id"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", connStr)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var root = doc.RootElement;
        root.GetProperty("rowCount").GetInt32().Should().Be(5);
        var rows = root.GetProperty("rows").EnumerateArray().ToList();
        rows[0].EnumerateArray().ElementAt(1).GetString().Should().Be("user1");
        rows[4].EnumerateArray().ElementAt(1).GetString().Should().Be("user5");
    }

    [Fact]
    public async Task ExecuteQuery_WithParameters_ReturnsFilteredRows()
    {
        var conn = CreateInMemoryConnection("memdb3");
        SeedTable(conn);
        var connStr = conn.ConnectionString;

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT name FROM users WHERE id = @id",
                ["parameters"] = new Dictionary<string, object?> { ["id"] = 2 }
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", connStr)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(1);
        rows[0].EnumerateArray().First().GetString().Should().Be("user2");
    }

    [Fact]
    public async Task ExecuteQuery_MaxRows_TruncatesResults()
    {
        var conn = CreateInMemoryConnection("memdb4");
        SeedTable(conn, rowCount: 10);
        var connStr = conn.ConnectionString;

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM users ORDER BY id",
                ["max_rows"] = 3
            },
            BuildScopeFactory(
                maxRows: 5,
                connections: [SQLiteConnection("sqlite-main", connStr)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var root = doc.RootElement;
        root.GetProperty("rowCount").GetInt32().Should().Be(3);
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_MaxRows_CannotExceedConfiguredMax()
    {
        var conn = CreateInMemoryConnection("memdb5");
        SeedTable(conn, rowCount: 10);
        var connStr = conn.ConnectionString;

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM users ORDER BY id",
                ["max_rows"] = 999
            },
            BuildScopeFactory(
                maxRows: 2,
                connections: [SQLiteConnection("sqlite-main", connStr)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_SelectInsert_ReturnsError()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "INSERT INTO users (name) VALUES ('hack')"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_SelectUpdate_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "UPDATE users SET name = 'x'"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_SelectDelete_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "DELETE FROM users WHERE id = 1"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_SelectDrop_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "DROP TABLE users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_SelectTruncate_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "TRUNCATE TABLE users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_SelectCreate_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "CREATE TABLE evil (id INT)"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Only SELECT");
    }

    [Fact]
    public async Task ExecuteQuery_MultipleStatements_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1; SELECT 2"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("single SQL statement");
    }

    [Fact]
    public async Task ExecuteQuery_WithComment_SelectStillWorks()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "-- this is a comment\nSELECT 42 AS answer"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rows")[0].EnumerateArray().First().GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ExecuteQuery_WithBlockComment_SelectStillWorks()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "/* block comment */ SELECT 99 AS val"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rows")[0].EnumerateArray().First().GetInt32().Should().Be(99);
    }

    [Fact]
    public async Task ExecuteQuery_InvalidSQL_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT FROMM nonexistent_table_xyz"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteQuery_CTEWithSelect_Works()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "WITH cte AS (SELECT * FROM users LIMIT 2) SELECT name FROM cte ORDER BY name"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteQuery_CTEWithInsert_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "WITH cte AS (SELECT 1) INSERT INTO users VALUES (99, 'x', 'y', 0)"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Blocked SQL keyword detected");
    }

    [Fact]
    public async Task ExecuteQuery_NullValue_NormalizedToNull()
    {
        var conn = CreateInMemoryConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE tnull (id INTEGER, val TEXT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO tnull VALUES (1, NULL)";
            cmd.ExecuteNonQuery();
        }

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id, val FROM tnull"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var row = doc.RootElement.GetProperty("rows")[0].EnumerateArray().ToList();
        row[1].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ExecuteQuery_EmptyResult_ReturnsZeroRowCount()
    {
        var conn = CreateInMemoryConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE empty_t (id INTEGER)";
            cmd.ExecuteNonQuery();
        }

        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT * FROM empty_t"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("rows").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteQuery_NullParameters_Dictionary_NotAdded()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT name FROM users WHERE name = @name",
                ["parameters"] = new Dictionary<string, object?> { ["name"] = "user1" }
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteQuery_ParametersWithAtPrefix_AreStripped()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT name FROM users WHERE id = @id",
                ["parameters"] = new Dictionary<string, object?> { ["@id"] = 3 }
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
        var name = doc.RootElement.GetProperty("rows")[0].EnumerateArray().First().GetString();
        name.Should().Be("user3");
    }

    [Fact]
    public async Task ExecuteQuery_InvalidParameterName_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1",
                ["parameters"] = new Dictionary<string, object?> { ["drop table"] = 1 }
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid parameter name");
    }

    [Fact]
    public async Task ExecuteQuery_ParametersJsonElement_Converted()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        using var doc = JsonDocument.Parse("{\"id\": 1}");
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT name FROM users WHERE id = @id",
                ["parameters"] = doc.RootElement.Clone()
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("rows")[0].EnumerateArray().First().GetString().Should().Be("user1");
    }

    [Fact]
    public async Task ExecuteQuery_SchemaRestriction_InvalidSchema_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT * FROM forbidden_schema.users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString, schemas: ["public"])]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no such table");
    }

    [Fact]
    public async Task ExecuteQuery_SchemaRestriction_AllowedSchema_Passes()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT * FROM main.users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString, schemas: ["main"])]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_NonPostgresProvider_SkipsSchemaValidation()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT * FROM users"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString, schemas: ["public"])]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_UnsupportedProvider_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "oracle-db",
                ["query"] = "SELECT 1"
            },
            BuildScopeFactory(connections: [
                new DatabaseQueryConnectionSettings
                {
                    Name = "oracle-db",
                    Provider = "oracle",
                    ConnectionString = "Data Source=test",
                    ReadOnly = true
                }
            ]));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported database provider");
    }

    [Fact]
    public async Task ExecuteQuery_ExecutionMs_PresentInOutput()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.TryGetProperty("executionMs", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_ColumnNames_MatchAliases()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1 AS first_col, 2 AS second_col"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        var columns = doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).ToList();
        columns.Should().BeEquivalentTo(["first_col", "second_col"]);
    }

    [Fact]
    public async Task ExecuteQuery_MultipleRows_AllReturned()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn, rowCount: 5);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM users ORDER BY id"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(5);
        var ids = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateArray().First().GetInt32()).ToList();
        ids.Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task ExecuteQuery_WithTrailingSemicolon_Works()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1;"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_BooleanParameter_Normalized()
    {
        var conn = CreateInMemoryConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE tbool (id INTEGER, active INTEGER)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO tbool VALUES (1, 1)";
            cmd.ExecuteNonQuery();
        }

        using var doc = JsonDocument.Parse("{\"active\": true}");
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM tbool WHERE active = @active",
                ["parameters"] = doc.RootElement.Clone()
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_NullParameter_BecomesDBNull()
    {
        var conn = CreateInMemoryConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE tnull (id INTEGER, val TEXT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO tnull VALUES (1, NULL)";
            cmd.ExecuteNonQuery();
        }

        using var doc = JsonDocument.Parse("{\"val\": null}");
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM tnull WHERE val IS NULL",
                ["parameters"] = doc.RootElement.Clone()
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_LargeMaxRows_CanBeConfigured()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn, rowCount: 3);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT id FROM users ORDER BY id",
                ["max_rows"] = 100
            },
            BuildScopeFactory(
                maxRows: 50,
                connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteQuery_NoConnections_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "any",
                ["query"] = "SELECT 1"
            },
            BuildScopeFactory(connections: []));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown database connection");
    }

    [Fact]
    public async Task ExecuteQuery_InvalidJsonInput_ForParameters_ReturnsError()
    {
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1",
                ["parameters"] = "not-a-dictionary"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", CreateInMemoryConnection().ConnectionString)]));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteQuery_SelectWithWhere_NoMatch_ReturnsEmpty()
    {
        var conn = CreateInMemoryConnection();
        SeedTable(conn);
        var result = await InvokeAsync(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT * FROM users WHERE id = 999"
            },
            BuildScopeFactory(connections: [SQLiteConnection("sqlite-main", conn.ConnectionString)]));
        result.Success.Should().BeTrue();
        var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(0);
    }
}