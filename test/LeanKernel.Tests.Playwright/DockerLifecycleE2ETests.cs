using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Npgsql;

using Xunit;

namespace LeanKernel.Tests.Playwright;

public sealed class DockerLifecycleE2ETests
{
    [Fact]
    public async Task RunningDockerDeployment_GatewayWebwrightToolCallsSucceed()
    {
        var config = DockerE2eConfig.FromEnvironment();
        if (!config.Enabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;

        await config.ValidatePreflightAsync(ct);

        using var gateway = BuildHttpClient(config.GatewayBaseUrl);

        var prompt =
            "Use the webwright MCP tool browser_run_task to open https://example.com and extract the page title. " +
            "Then call browser_get_run for the returned runId to confirm the run succeeded. " +
            "Reply exactly as webwright-success:<title>.";

        var responseText = await SubmitResponsesAsync(
            gateway,
            config.Model,
            prompt,
            bearerToken: null,
            ct);

        Assert.Contains("webwright-success", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Example Domain", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("task was canceled", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mcp tool invocation failed", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunningDockerDeployment_ExecutesFullLifecycle()
    {
        var config = DockerE2eConfig.FromEnvironment();
        if (!config.Enabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;

        await config.ValidatePreflightAsync(ct);

        var runId = Guid.NewGuid().ToString("N");
        var retrievalToken = $"retrieval-{runId}";
        var persistenceFactValue = $"beacon-{runId[..8]}";
        var issuer = "lk-e2e";
        var subject = $"subject-{runId}";

        await using var db = new NpgsqlConnection(config.PostgresConnectionString);
        await db.OpenAsync(ct);

        var identity = await CreateIdentityAsync(db, config, issuer, subject, ct);
        TestIdentity? activeIdentity = null;

        try
        {
            using var gateway = BuildHttpClient(config.GatewayBaseUrl);
            using var gbrain = BuildHttpClient(config.GBrainBaseUrl, config.GBrainAuthToken);

            var requestStartedAt = DateTimeOffset.UtcNow.AddSeconds(-2);

            _ = await SubmitResponsesAsync(
                gateway,
                config.Model,
                "Initialize session for docker lifecycle e2e.",
                bearerToken: null,
                ct);

            activeIdentity = await ResolveLatestAnonymousIdentityAsync(
                db,
                identity.TenantId,
                identity.ChannelId,
                requestStartedAt,
                ct);

            var scopeIdentity = activeIdentity ?? identity;

            var seededKey = $"facts/what/e2e/{runId}/seed";
            var seededSlug = BuildScopedSlug(scopeIdentity, seededKey);
            var seededPageContent =
                $"Fact: lifecycle retrieval token is {retrievalToken}. Source: docker-e2e run {runId}.";

            await PutPageAsync(gbrain, seededSlug, seededPageContent, ct);

            var seededSearch = await SearchScopedAsync(gbrain, scopeIdentity, retrievalToken, ct);
            Assert.Contains(seededSearch, match =>
                string.Equals(match.Slug, seededSlug, StringComparison.Ordinal));

            var retrievalPrompt =
                "What is the lifecycle retrieval token stored in memory? Return only the token.";
            var retrievalText = await SubmitResponsesAsync(
                gateway,
                config.Model,
                retrievalPrompt,
                bearerToken: null,
                ct);

            Assert.Contains("retrieval-", retrievalText, StringComparison.OrdinalIgnoreCase);

            var prePersistenceMatches = await SearchScopedAsync(gbrain, scopeIdentity, "beacon", ct);
            var baselineSlugs = prePersistenceMatches
                .Select(match => match.Slug)
                .Where(static slug => !string.IsNullOrWhiteSpace(slug))
                .ToHashSet(StringComparer.Ordinal);
            baselineSlugs.Add(seededSlug);

            var persistencePrompt =
                $"Remember this fact for future responses: project_beacon is {persistenceFactValue}. " +
                $"Reply with exactly '{persistenceFactValue}'.";
            var persistenceText = await SubmitResponsesAsync(
                gateway,
                config.Model,
                persistencePrompt,
                bearerToken: null,
                ct);

            Assert.Contains(persistenceFactValue, persistenceText, StringComparison.OrdinalIgnoreCase);

            var persisted = await WaitForPersistenceAsync(
                gbrain,
                scopeIdentity,
                baselineSlugs,
                persistenceFactValue,
                TimeSpan.FromSeconds(45),
                ct);

            if (config.RequirePersistenceCheck)
            {
                Assert.True(
                    persisted,
                    $"Did not observe persisted memory page creation or persisted fact '{persistenceFactValue}' within timeout.");
            }
        }
        finally
        {
            if (activeIdentity is { } discovered && discovered.UserId != identity.UserId)
            {
                await CleanupUserSessionsAsync(db, discovered.UserId, ct);
            }

            await CleanupIdentityAsync(db, identity, ct);
        }
    }

    private static HttpClient BuildHttpClient(string baseUrl, string? bearerToken = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return client;
    }

    private static async Task<TestIdentity> CreateIdentityAsync(
        NpgsqlConnection db,
        DockerE2eConfig config,
        string issuer,
        string subject,
        CancellationToken ct)
    {
        var tenantId = await ResolveTenantIdAsync(db, config.GatewayHostName, ct)
            ?? await ResolveTenantIdAsync(db, "localhost", ct)
            ?? throw new InvalidOperationException(
                $"No active tenant found for host '{config.GatewayHostName}' or fallback 'localhost'.");

        var channelId = await ResolveChannelIdAsync(db, "openai-http", ct)
            ?? throw new InvalidOperationException("Could not resolve 'openai-http' channel id.");

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var userCommand = db.CreateCommand())
        {
            userCommand.CommandText =
                """
                INSERT INTO "Users"
                ("Id", "Email", "UserName", "FirstName", "LastName", "FullName", "IsActive", "IsLockedOut", "LastActivity", "CreatedOn", "CreatedBy_Id", "CreatedBy_Email", "CreatedBy_FullName", "UpdatedOn", "UpdatedBy_Id", "UpdatedBy_Email", "UpdatedBy_FullName", "IsDeleted", "Issuer", "Subject", "IsGuest", "PersonId")
                VALUES
                (@id, @email, @userName, @firstName, @lastName, @fullName, TRUE, FALSE, @lastActivity, @createdOn, @createdById, @createdByEmail, @createdByFullName, NULL, NULL, NULL, NULL, FALSE, @issuer, @subject, FALSE, @personId);
                """;
            userCommand.Parameters.AddWithValue("id", userId);
            userCommand.Parameters.AddWithValue("email", $"{subject}@e2e.local");
            userCommand.Parameters.AddWithValue("userName", $"user-{subject}");
            userCommand.Parameters.AddWithValue("firstName", "Docker");
            userCommand.Parameters.AddWithValue("lastName", "E2E");
            userCommand.Parameters.AddWithValue("fullName", "Docker E2E User");
            userCommand.Parameters.AddWithValue("lastActivity", now);
            userCommand.Parameters.AddWithValue("createdOn", now);
            userCommand.Parameters.AddWithValue("createdById", Guid.Empty);
            userCommand.Parameters.AddWithValue("createdByEmail", "system@leankernel.local");
            userCommand.Parameters.AddWithValue("createdByFullName", "System");
            userCommand.Parameters.AddWithValue("issuer", issuer);
            userCommand.Parameters.AddWithValue("subject", subject);
            userCommand.Parameters.AddWithValue("personId", userId);
            await userCommand.ExecuteNonQueryAsync(ct);
        }

        var bindingId = Guid.NewGuid();

        await using (var bindingCommand = db.CreateCommand())
        {
            bindingCommand.CommandText =
                """
                INSERT INTO "ChannelSenderBindings"
                ("Id", "TenantId", "UserId", "ChannelId", "Issuer", "Subject", "BearerToken", "IsActive", "CreatedOn")
                VALUES
                (@id, @tenantId, @userId, @channelId, @issuer, @subject, @bearerToken, TRUE, @createdOn);
                """;
            bindingCommand.Parameters.AddWithValue("id", bindingId);
            bindingCommand.Parameters.AddWithValue("tenantId", tenantId);
            bindingCommand.Parameters.AddWithValue("userId", userId);
            bindingCommand.Parameters.AddWithValue("channelId", channelId);
            bindingCommand.Parameters.AddWithValue("issuer", issuer);
            bindingCommand.Parameters.AddWithValue("subject", subject);
            bindingCommand.Parameters.AddWithValue("bearerToken", $"e2e-{subject}");
            bindingCommand.Parameters.AddWithValue("createdOn", now);
            await bindingCommand.ExecuteNonQueryAsync(ct);
        }

        return new TestIdentity(tenantId, userId, userId, channelId, bindingId);
    }

    private static async Task<Guid?> ResolveTenantIdAsync(
        NpgsqlConnection db,
        string hostName,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            "SELECT \"Id\" FROM \"Tenants\" WHERE \"HostName\" = @host AND \"IsActive\" = TRUE LIMIT 1;";
        command.Parameters.AddWithValue("host", hostName);

        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static async Task<Guid?> ResolveChannelIdAsync(
        NpgsqlConnection db,
        string channelName,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            "SELECT \"Id\" FROM \"Channels\" WHERE \"Name\" = @name LIMIT 1;";
        command.Parameters.AddWithValue("name", channelName);

        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static async Task<TestIdentity?> ResolveLatestAnonymousIdentityAsync(
        NpgsqlConnection db,
        Guid tenantId,
        Guid channelId,
        DateTimeOffset createdAfter,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText =
            """
            SELECT s."TenantId", s."UserId", s."ChannelId", u."PersonId"
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

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new TestIdentity(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(3),
            reader.GetGuid(2),
            Guid.Empty);
    }

    private static async Task CleanupIdentityAsync(
        NpgsqlConnection db,
        TestIdentity identity,
        CancellationToken ct)
    {
        await CleanupUserSessionsAsync(db, identity.UserId, ct);

        await using var command = db.CreateCommand();
        command.CommandText =
            """
            DELETE FROM "ChannelSenderBindings" WHERE "Id" = @bindingId;
            DELETE FROM "Users" WHERE "Id" = @userId;
            """;
        command.Parameters.AddWithValue("bindingId", identity.BindingId);
        command.Parameters.AddWithValue("userId", identity.UserId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task CleanupUserSessionsAsync(
        NpgsqlConnection db,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM \"Sessions\" WHERE \"UserId\" = @userId;";
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static async Task<string> SubmitResponsesAsync(
        HttpClient gateway,
        string model,
        string input,
        string? bearerToken,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        request.Content = JsonContent.Create(new
        {
            model,
            input,
            agent = new
            {
                name = "leankernel"
            }
        });

        using var response = await gateway.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Gateway /v1/responses failed with {(int)response.StatusCode}: {body}");

        var outputText = ExtractOutputText(body);
        Assert.False(string.IsNullOrWhiteSpace(outputText), "Gateway response text was empty.");
        return outputText;
    }

    private static string ExtractOutputText(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            return payload;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("type", out var type)
                    || !string.Equals(type.GetString(), "output_text", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!contentItem.TryGetProperty("text", out var text))
                {
                    continue;
                }

                var value = text.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(value);
            }
        }

        return builder.Length > 0 ? builder.ToString() : payload;
    }

    private static async Task PutPageAsync(
        HttpClient gbrain,
        string slug,
        string content,
        CancellationToken ct)
    {
        _ = await CallToolAsync(gbrain, "put_page", new { slug, content }, ct);
    }

    private static async Task<IReadOnlyList<SearchMatch>> SearchScopedAsync(
        HttpClient gbrain,
        TestIdentity identity,
        string query,
        CancellationToken ct)
    {
        var namespaceName = BuildScopedNamespace(identity);
        var result = await CallToolAsync(
            gbrain,
            "search",
            new
            {
                query,
                limit = 25,
                namespace_name = namespaceName
            },
            ct);

        return ParseSearchMatches(result);
    }

    private static async Task<bool> WaitForPersistenceAsync(
        HttpClient gbrain,
        TestIdentity identity,
        IReadOnlySet<string> baselineSlugs,
        string persistenceFactValue,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var beaconMatches = await SearchScopedAsync(gbrain, identity, "beacon", ct);
            var factMatches = await SearchScopedAsync(gbrain, identity, persistenceFactValue, ct);
            var matches = beaconMatches.Concat(factMatches).ToList();

            var hasNewMemoryPage = matches.Any(match =>
                !string.IsNullOrWhiteSpace(match.Slug)
                && !baselineSlugs.Contains(match.Slug));

            var hasPersistedFact = matches.Any(match =>
                match.Content.Contains(persistenceFactValue, StringComparison.OrdinalIgnoreCase));

            if (hasNewMemoryPage || hasPersistedFact)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return false;
    }

    private static string BuildScopedNamespace(TestIdentity identity)
    {
        return $"memory/{identity.TenantId:D}/{identity.PersonId:D}/{identity.ChannelId:D}";
    }

    private static string BuildScopedSlug(TestIdentity identity, string key)
    {
        return $"{BuildScopedNamespace(identity)}/{key}";
    }

    private static async Task<JsonElement> CallToolAsync(
        HttpClient gbrain,
        string name,
        object args,
        CancellationToken ct)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "tools/call",
            @params = new
            {
                name,
                arguments = args
            }
        };

        using var response = await gbrain.PostAsJsonAsync("/mcp", request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.IsSuccessStatusCode,
            $"GBrain MCP call '{name}' failed with {(int)response.StatusCode}: {body}");

        var root = ParseRpcResponse(body);

        if (root.TryGetProperty("error", out var error))
        {
            Assert.Fail($"GBrain MCP call '{name}' returned error: {error.GetRawText()}");
        }

        Assert.True(
            root.TryGetProperty("result", out var rpcResult),
            $"GBrain MCP call '{name}' did not include a result payload.");

        return UnwrapToolResult(rpcResult);
    }

    private static JsonElement ParseRpcResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            Assert.Fail("GBrain MCP returned an empty response body.");
        }

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            using var jsonDocument = JsonDocument.Parse(body);
            return jsonDocument.RootElement.Clone();
        }

        JsonElement? lastData = null;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[6..].Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            using var dataDocument = JsonDocument.Parse(json);
            lastData = dataDocument.RootElement.Clone();
        }

        if (lastData is null)
        {
            Assert.Fail($"GBrain MCP returned non-JSON/non-SSE body: {body}");
        }

        return lastData.Value;
    }

    private static JsonElement UnwrapToolResult(JsonElement rpcResult)
    {
        if (rpcResult.ValueKind != JsonValueKind.Object)
        {
            return rpcResult.Clone();
        }

        if (rpcResult.TryGetProperty("structuredContent", out var structuredContent)
            && structuredContent.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return structuredContent.Clone();
        }

        if (rpcResult.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type)
                    || !string.Equals(type.GetString(), "text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("text", out var textElement))
                {
                    continue;
                }

                var text = textElement.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                try
                {
                    using var parsed = JsonDocument.Parse(text);
                    return parsed.RootElement.Clone();
                }
                catch (JsonException)
                {
                    using var wrapped = JsonDocument.Parse(JsonSerializer.Serialize(text));
                    return wrapped.RootElement.Clone();
                }
            }
        }

        return rpcResult.Clone();
    }

    private static IReadOnlyList<SearchMatch> ParseSearchMatches(JsonElement result)
    {
        var items = result.ValueKind switch
        {
            JsonValueKind.Array => result.EnumerateArray().ToList(),
            JsonValueKind.Object when result.TryGetProperty("results", out var nested)
                && nested.ValueKind == JsonValueKind.Array => nested.EnumerateArray().ToList(),
            _ => []
        };

        return items
            .Select(ToSearchMatch)
            .Where(match => !string.IsNullOrWhiteSpace(match.Slug))
            .ToList();
    }

    private static SearchMatch ToSearchMatch(JsonElement item)
    {
        var slug = item.TryGetProperty("slug", out var slugElement)
            ? slugElement.GetString() ?? string.Empty
            : string.Empty;

        var content = ReadBestContent(item);
        var score = item.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetDouble(out var value)
            ? value
            : 0.0;

        return new SearchMatch(slug, content, score);
    }

    private static string ReadBestContent(JsonElement item)
    {
        static string ReadString(JsonElement source, string propertyName)
        {
            return source.TryGetProperty(propertyName, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        var compiled = ReadString(item, "compiled_truth");
        if (!string.IsNullOrWhiteSpace(compiled))
        {
            return compiled;
        }

        var chunk = ReadString(item, "chunk_text");
        if (!string.IsNullOrWhiteSpace(chunk))
        {
            return chunk;
        }

        var content = ReadString(item, "content");
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return ReadString(item, "title");
    }

    private readonly record struct TestIdentity(
        Guid TenantId,
        Guid UserId,
        Guid PersonId,
        Guid ChannelId,
        Guid BindingId);

    private readonly record struct SearchMatch(string Slug, string Content, double Score);

    private sealed class DockerE2eConfig
    {
        public const string EnabledEnvVar = "LEANKERNEL_DOCKER_E2E_ENABLED";

        public bool Enabled { get; }

        public string GatewayBaseUrl { get; }

        public string GBrainBaseUrl { get; }

        public string PostgresConnectionString { get; }

        public string Model { get; }

        public string? GBrainAuthToken { get; }

        public bool RequirePersistenceCheck { get; }

        public string GatewayHostName => new Uri(GatewayBaseUrl).Host;

        public static DockerE2eConfig FromEnvironment()
        {
            var enabledRaw = Environment.GetEnvironmentVariable(EnabledEnvVar);
            var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase);

            var gatewayBaseUrl =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_GATEWAY_URL")
                ?? Environment.GetEnvironmentVariable("LEANKERNEL_BASE_URL")
                ?? "http://localhost:8080";
            var gbrainBaseUrl =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_GBRAIN_URL")
                ?? "http://localhost:8789";
            var postgresConnectionString =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_POSTGRES_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=leankernel;Username=leankernel;Password=leankernel-dev-password";
            var model =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_MODEL")
                ?? "medium";
            var gbrainAuthToken = ResolveGbrainAuthToken();
            var requirePersistenceCheck =
                string.Equals(
                    Environment.GetEnvironmentVariable("LEANKERNEL_E2E_REQUIRE_PERSISTENCE"),
                    "true",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    Environment.GetEnvironmentVariable("LEANKERNEL_E2E_REQUIRE_PERSISTENCE"),
                    "1",
                    StringComparison.OrdinalIgnoreCase);

            return new DockerE2eConfig(
                enabled,
                gatewayBaseUrl,
                gbrainBaseUrl,
                postgresConnectionString,
                model,
                gbrainAuthToken,
                requirePersistenceCheck);
        }

        public async Task ValidatePreflightAsync(CancellationToken ct)
        {
            using var gateway = BuildHttpClient(GatewayBaseUrl);
            using var gbrain = BuildHttpClient(GBrainBaseUrl, GBrainAuthToken);
            await using var db = new NpgsqlConnection(PostgresConnectionString);

            using (var health = await gateway.GetAsync("/health", ct))
            {
                Assert.True(
                    health.IsSuccessStatusCode,
                    $"Gateway health check failed: {(int)health.StatusCode}");
            }

            using (var health = await gbrain.GetAsync("/health", ct))
            {
                Assert.True(
                    health.IsSuccessStatusCode,
                    $"GBrain health check failed: {(int)health.StatusCode}");
            }

            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            var value = await cmd.ExecuteScalarAsync(ct);

            Assert.Equal(1, Convert.ToInt32(value));
        }

        private DockerE2eConfig(
            bool enabled,
            string gatewayBaseUrl,
            string gbrainBaseUrl,
            string postgresConnectionString,
            string model,
            string? gbrainAuthToken,
            bool requirePersistenceCheck)
        {
            Enabled = enabled;
            GatewayBaseUrl = gatewayBaseUrl;
            GBrainBaseUrl = gbrainBaseUrl;
            PostgresConnectionString = postgresConnectionString;
            Model = model;
            GBrainAuthToken = gbrainAuthToken;
            RequirePersistenceCheck = requirePersistenceCheck;
        }

        private static string? ResolveGbrainAuthToken()
        {
            var fromEnv = Environment.GetEnvironmentVariable("LEANKERNEL_E2E_GBRAIN_AUTH_TOKEN");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv;
            }

            var explicitFile = Environment.GetEnvironmentVariable("LEANKERNEL_E2E_GBRAIN_TOKEN_FILE");
            if (!string.IsNullOrWhiteSpace(explicitFile))
            {
                var explicitToken = TryReadTokenFile(explicitFile);
                if (!string.IsNullOrWhiteSpace(explicitToken))
                {
                    return explicitToken;
                }
            }

            var localTokenFile = Path.Combine(Environment.CurrentDirectory, "data", "gbrain", ".engine-token");
            return TryReadTokenFile(localTokenFile);
        }

        private static string? TryReadTokenFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var token = File.ReadAllText(filePath).Trim();
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
            catch
            {
                return null;
            }
        }
    }
}