using System.Net.Http.Json;

using Npgsql;

using Xunit;

namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Validates endpoint-level policy behavior and durable event spine persistence
/// against the dockerized runtime.
/// </summary>
public sealed class PolicyAndEventStoreEndpointTests
{
    [Fact]
    public async Task RunningDockerDeployment_ResponsesPersistDurableEventSpineRows()
    {
        var config = DockerEndpointConfig.FromEnvironment();
        if (!config.Enabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = cts.Token;

        await config.ValidatePreflightAsync(ct);

        var requestStartedAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        using (var client = BuildGatewayClient(config.GatewayBaseUrl))
        {
            var text = await SubmitResponsesAsync(client, config.Model, "Persist event spine validation turn.", ct);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }

        await using var db = new NpgsqlConnection(config.PostgresConnectionString);
        await db.OpenAsync(ct);

        var tenantId = await ResolveTenantIdAsync(db, config.GatewayHostName, ct)
            ?? await ResolveTenantIdAsync(db, "localhost", ct)
            ?? throw new InvalidOperationException("Could not resolve tenant id for gateway host.");

        var channelId = await ResolveChannelIdAsync(db, "openai-http", ct)
            ?? throw new InvalidOperationException("Could not resolve openai-http channel id.");

        var sessionId = await ResolveLatestAnonymousSessionIdAsync(db, tenantId, channelId, requestStartedAt, ct)
            ?? throw new InvalidOperationException("Could not resolve latest anonymous session id.");

        var (total, turnCount) = await CountEventSpineRowsForSessionAsync(db, sessionId, ct);

        Assert.True(total > 0, $"Expected durable event rows for session '{sessionId}' but found none.");
        Assert.True(turnCount > 0, $"Expected at least one 'turn' event for session '{sessionId}'.");
    }

    [Fact]
    public async Task RunningDockerDeployment_AnonymousSessionsRemainIsolatedAcrossClients()
    {
        var config = DockerEndpointConfig.FromEnvironment();
        if (!config.Enabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = cts.Token;

        await config.ValidatePreflightAsync(ct);

        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        using (var clientA = BuildGatewayClient(config.GatewayBaseUrl))
        using (var clientB = BuildGatewayClient(config.GatewayBaseUrl))
        {
            _ = await SubmitResponsesAsync(clientA, config.Model, "Anonymous isolation probe A.", ct);
            _ = await SubmitResponsesAsync(clientB, config.Model, "Anonymous isolation probe B.", ct);
        }

        await using var db = new NpgsqlConnection(config.PostgresConnectionString);
        await db.OpenAsync(ct);

        var tenantId = await ResolveTenantIdAsync(db, config.GatewayHostName, ct)
            ?? await ResolveTenantIdAsync(db, "localhost", ct)
            ?? throw new InvalidOperationException("Could not resolve tenant id for gateway host.");

        var channelId = await ResolveChannelIdAsync(db, "openai-http", ct)
            ?? throw new InvalidOperationException("Could not resolve openai-http channel id.");

        var userCount = await CountDistinctAnonymousUsersSinceAsync(db, tenantId, channelId, startedAt, ct);

        Assert.True(
            userCount >= 2,
            $"Expected at least two distinct anonymous users (isolated sessions), but found {userCount}.");
    }

    private static HttpClient BuildGatewayClient(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120),
        };
    }

    private static async Task<string> SubmitResponsesAsync(
        HttpClient gateway,
        string model,
        string input,
        CancellationToken ct)
    {
        var request = new
        {
            model,
            input,
            agent = new
            {
                name = "leankernel",
            },
        };

        using var response = await gateway.PostAsJsonAsync("/v1/responses", request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.IsSuccessStatusCode, $"/v1/responses failed with {(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task<Guid?> ResolveTenantIdAsync(NpgsqlConnection db, string hostName, CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            "SELECT \"Id\" FROM \"Tenants\" WHERE \"HostName\" = @host AND \"IsActive\" = TRUE LIMIT 1;";
        command.Parameters.AddWithValue("host", hostName);

        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static async Task<Guid?> ResolveChannelIdAsync(NpgsqlConnection db, string channelName, CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT \"Id\" FROM \"Channels\" WHERE \"Name\" = @name LIMIT 1;";
        command.Parameters.AddWithValue("name", channelName);

        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static async Task<string?> ResolveLatestAnonymousSessionIdAsync(
        NpgsqlConnection db,
        Guid tenantId,
        Guid channelId,
        DateTimeOffset createdAfter,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            """
            SELECT s."Id"
            FROM "Sessions" s
            JOIN "Users" u ON u."Id" = s."UserId"
            WHERE s."TenantId" = @tenantId
              AND s."ChannelId" = @channelId
              AND s."CreatedAt" >= @createdAfter
              AND u."Issuer" = 'anonymous'
              AND u."IsGuest" = TRUE
            ORDER BY s."CreatedAt" DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("channelId", channelId);
        command.Parameters.AddWithValue("createdAfter", createdAfter);

        var result = await command.ExecuteScalarAsync(ct);
        return result?.ToString();
    }

    private static async Task<(int Total, int TurnCount)> CountEventSpineRowsForSessionAsync(
        NpgsqlConnection db,
        string sessionId,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(*)::int AS total,
                COUNT(*) FILTER (WHERE "EventType" = 'turn')::int AS turn_count
            FROM "Events"
            WHERE "SessionId" = @sessionId;
            """;
        command.Parameters.AddWithValue("sessionId", sessionId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return (0, 0);
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<int> CountDistinctAnonymousUsersSinceAsync(
        NpgsqlConnection db,
        Guid tenantId,
        Guid channelId,
        DateTimeOffset createdAfter,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(DISTINCT s."UserId")::int
            FROM "Sessions" s
            JOIN "Users" u ON u."Id" = s."UserId"
            WHERE s."TenantId" = @tenantId
              AND s."ChannelId" = @channelId
              AND s."CreatedAt" >= @createdAfter
              AND u."Issuer" = 'anonymous'
              AND u."IsGuest" = TRUE;
            """;
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("channelId", channelId);
        command.Parameters.AddWithValue("createdAfter", createdAfter);

        var result = await command.ExecuteScalarAsync(ct);
        return result is int value ? value : Convert.ToInt32(result);
    }

    private sealed class DockerEndpointConfig
    {
        private DockerEndpointConfig(
            bool enabled,
            string gatewayBaseUrl,
            string postgresConnectionString,
            string model)
        {
            Enabled = enabled;
            GatewayBaseUrl = gatewayBaseUrl;
            PostgresConnectionString = postgresConnectionString;
            Model = model;
        }

        public bool Enabled { get; }

        public string GatewayBaseUrl { get; }

        public string PostgresConnectionString { get; }

        public string Model { get; }

        public string GatewayHostName => new Uri(GatewayBaseUrl).Host;

        public static DockerEndpointConfig FromEnvironment()
        {
            var enabledRaw = Environment.GetEnvironmentVariable("LEANKERNEL_DOCKER_E2E_ENABLED");
            var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase);

            var gatewayBaseUrl =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_GATEWAY_URL")
                ?? Environment.GetEnvironmentVariable("LEANKERNEL_BASE_URL")
                ?? "http://localhost:8080";

            var postgresConnectionString =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_POSTGRES_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=leankernel;Username=leankernel;Password=leankernel-dev-password";

            var model =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_MODEL")
                ?? "medium";

            return new DockerEndpointConfig(enabled, gatewayBaseUrl, postgresConnectionString, model);
        }

        public async Task ValidatePreflightAsync(CancellationToken ct)
        {
            using var gateway = BuildGatewayClient(GatewayBaseUrl);
            await using var db = new NpgsqlConnection(PostgresConnectionString);

            using (var health = await gateway.GetAsync("/health", ct))
            {
                Assert.True(health.IsSuccessStatusCode, $"Gateway health check failed: {(int)health.StatusCode}");
            }

            await db.OpenAsync(ct);
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT 1;";
            var value = await command.ExecuteScalarAsync(ct);
            Assert.Equal(1, Convert.ToInt32(value));
        }
    }
}
