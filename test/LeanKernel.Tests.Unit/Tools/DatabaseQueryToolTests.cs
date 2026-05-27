using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public class DatabaseQueryToolTests
{
    [Fact]
    public async Task DatabaseQueryTool_returns_validation_error_when_connection_is_missing()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "SELECT 1" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Connection is required");
    }

    [Fact]
    public async Task DatabaseQueryTool_returns_error_when_connection_is_unknown()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "missing",
                ["query"] = "SELECT 1"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Unknown database connection: missing");
    }

    [Fact]
    public async Task DatabaseQueryTool_returns_error_for_unsupported_provider()
    {
        var unsupported = new DatabaseQueryConnectionConfig
        {
            Name = "bad",
            Provider = "sqlserver",
            ConnectionString = "Server=localhost;",
            ReadOnly = true
        };
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(unsupported));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "bad",
                ["query"] = "SELECT 1"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported database provider");
    }

    [Fact]
    public async Task DatabaseQueryTool_requires_read_only_connection()
    {
        var writable = new DatabaseQueryConnectionConfig
        {
            Name = "sqlite-rw",
            Provider = "sqlite",
            ConnectionString = "Data Source=file:readonly-check?mode=memory&cache=shared",
            ReadOnly = false
        };
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(writable));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-rw",
                ["query"] = "SELECT 1"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("readOnly=true");
    }

    [Theory]
    [InlineData("UPDATE users SET name = 'x'")]
    [InlineData("SELECT 1 COPY users TO '/tmp/x'")]
    [InlineData("SELECT DO $$ BEGIN END $$")]
    [InlineData("SELECT CALL do_stuff()")]
    [InlineData("SELECT EXECUTE stmt")]
    public async Task DatabaseQueryTool_blocks_disallowed_keywords(string query)
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = query
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        (result.Error!.Contains("Blocked SQL keyword detected", StringComparison.Ordinal)
            || result.Error.Contains("Only SELECT, WITH ... SELECT, or EXPLAIN statements are allowed", StringComparison.Ordinal))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task DatabaseQueryTool_blocks_cte_dml_patterns()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "WITH x AS (INSERT INTO users VALUES (1)) SELECT * FROM x"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Blocked SQL keyword detected");
    }

    [Fact]
    public async Task DatabaseQueryTool_blocks_multiple_statements()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1; SELECT 2"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Only a single SQL statement is allowed");
    }

    [Fact]
    public async Task DatabaseQueryTool_executes_parameterized_sqlite_query()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT @id AS id, @name AS name",
                ["parameters"] = new Dictionary<string, object?> { ["id"] = 42, ["name"] = "leankernel" }
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();

        using var document = JsonDocument.Parse(result.Output!);
        document.RootElement.GetProperty("columns").EnumerateArray().Select(column => column.GetString()).Should().Equal("id", "name");
        document.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("rows")[0][0].GetInt64().Should().Be(42);
        document.RootElement.GetProperty("rows")[0][1].GetString().Should().Be("leankernel");
    }

    [Fact]
    public async Task DatabaseQueryTool_honors_row_limit_and_sets_truncated()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection(), maxRows: 2));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 4) SELECT x FROM c"
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        using var document = JsonDocument.Parse(result.Output!);
        document.RootElement.GetProperty("rowCount").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DatabaseQueryTool_allows_blocked_words_inside_strings_and_comments()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 'DROP TABLE x' AS text -- DELETE comment"
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DatabaseQueryTool_enforces_postgres_schema_allowlist()
    {
        var postgres = new DatabaseQueryConnectionConfig
        {
            Name = "pg",
            Provider = "postgres",
            ConnectionString = "Host=localhost;Database=fake;Username=fake;Password=fake",
            ReadOnly = true,
            AllowedSchemas = ["public"]
        };
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(postgres));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "pg",
                ["query"] = "SELECT * FROM private.users"
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Schema 'private' is not allowed for this connection");
    }

    [Fact]
    public async Task DatabaseQueryTool_rejects_non_positive_timeout()
    {
        var tool = DatabaseQueryTool.Create(CreateScopeFactory(DefaultSqliteConnection()));

        var result = await tool.Handler!(
            new Dictionary<string, object?>
            {
                ["connection"] = "sqlite-main",
                ["query"] = "SELECT 1",
                ["timeout_seconds"] = 0
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("timeout_seconds must be greater than zero");
    }

    private static IServiceScopeFactory CreateScopeFactory(DatabaseQueryConnectionConfig connectionConfig, int maxRows = 200, int defaultTimeoutSeconds = 30)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<LeanKernelConfig>(config =>
        {
            config.DatabaseQuery.MaxRows = maxRows;
            config.DatabaseQuery.DefaultTimeoutSeconds = defaultTimeoutSeconds;
            config.DatabaseQuery.Connections =
            [
                connectionConfig
            ];
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static DatabaseQueryConnectionConfig DefaultSqliteConnection()
    {
        return new DatabaseQueryConnectionConfig
        {
            Name = "sqlite-main",
            Provider = "sqlite",
            ConnectionString = "Data Source=file:db-query-tests?mode=memory&cache=shared",
            ReadOnly = true,
            AllowedSchemas = []
        };
    }
}
