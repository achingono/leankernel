using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace LeanKernel.Tests.Playwright;

public sealed class DockerWebwrightE2ETests
{
    [Fact]
    public async Task RunningDockerDeployment_WebwrightMcpLifecycleSucceeds()
    {
        var config = DockerWebwrightE2eConfig.FromEnvironment();
        if (!config.Enabled)
        {
            return;
        }

        using var cts = new CancellationTokenSource(config.RunTimeout + TimeSpan.FromMinutes(2));
        var ct = cts.Token;

        await config.ValidatePreflightAsync(ct);

        using var webwright = BuildHttpClient(config.WebwrightBaseUrl);
        string? sessionId = null;

        try
        {
            sessionId = await InitializeSessionAsync(webwright, ct);
            await SendInitializedNotificationAsync(webwright, sessionId, ct);

            var toolNames = await ListToolsAsync(webwright, sessionId, ct);
            Assert.Contains("browser_run_task", toolNames);
            Assert.Contains("browser_get_run", toolNames);
            Assert.Contains("browser_cancel_run", toolNames);
            Assert.Contains("browser_get_artifact", toolNames);

            var runResult = await CallToolAsync(
                webwright,
                sessionId,
                "browser_run_task",
                new
                {
                    task = "Capture a screenshot for docker e2e validation."
                },
                ct);

            Assert.True(runResult.TryGetProperty("runId", out var runIdValue),
                $"browser_run_task did not return runId: {runResult.GetRawText()}");
            var runId = runIdValue.GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId),
                $"browser_run_task returned invalid runId: {runResult.GetRawText()}");

            var runState = await WaitForRunCompletionAsync(webwright, sessionId, runId!, config.RunTimeout, ct);
            Assert.True(string.Equals(ReadString(runState, "status"), "succeeded", StringComparison.OrdinalIgnoreCase),
                $"Expected succeeded run status but got: {runState.GetRawText()}");

            Assert.True(runState.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array,
                $"browser_get_run did not include artifacts array: {runState.GetRawText()}");
            var firstArtifact = artifacts.EnumerateArray().FirstOrDefault();
            Assert.True(firstArtifact.ValueKind == JsonValueKind.Object,
                $"browser_get_run returned empty artifacts for run '{runId}': {runState.GetRawText()}");

            var artifactId = ReadString(firstArtifact, "id");
            Assert.False(string.IsNullOrWhiteSpace(artifactId),
                $"Artifact id missing in run state: {runState.GetRawText()}");

            var artifact = await CallToolAsync(
                webwright,
                sessionId,
                "browser_get_artifact",
                new
                {
                    runId,
                    artifactId
                },
                ct);

            Assert.Equal(artifactId, ReadString(artifact, "id"));
            Assert.Equal("image/png", ReadString(artifact, "contentType"));

            var dataBase64 = ReadString(artifact, "dataBase64");
            Assert.False(string.IsNullOrWhiteSpace(dataBase64),
                $"browser_get_artifact returned empty payload: {artifact.GetRawText()}");

            var bytes = Convert.FromBase64String(dataBase64);
            Assert.NotEmpty(bytes);

            var expectedBytes = artifact.TryGetProperty("bytes", out var byteCountElement)
                ? byteCountElement.GetInt32()
                : 0;
            Assert.Equal(expectedBytes, bytes.Length);

            Assert.True(
                bytes.Length > 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4E
                && bytes[3] == 0x47,
                "Artifact payload is not a PNG signature.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await DeleteSessionAsync(webwright, sessionId!, cleanupCts.Token);
                }
                catch
                {
                }
            }
        }
    }

    private static HttpClient BuildHttpClient(string baseUrl)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };

        return client;
    }

    private static async Task<string> InitializeSessionAsync(HttpClient webwright, CancellationToken ct)
    {
        var response = await SendRpcAsync(
            webwright,
            method: "initialize",
            parameters: new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new
                {
                    name = "leankernel-docker-e2e",
                    version = "1.0.0"
                }
            },
            sessionId: null,
            ct);

        Assert.True(response.Payload.TryGetProperty("result", out _),
            $"initialize did not return result payload: {response.Payload.GetRawText()}");

        Assert.False(string.IsNullOrWhiteSpace(response.SessionId),
            $"initialize did not return mcp-session-id header: {response.Payload.GetRawText()}");

        return response.SessionId!;
    }

    private static async Task<IReadOnlyList<string>> ListToolsAsync(HttpClient webwright, string sessionId, CancellationToken ct)
    {
        var response = await SendRpcAsync(webwright, "tools/list", null, sessionId, ct);
        Assert.True(response.Payload.TryGetProperty("result", out var result),
            $"tools/list did not return result payload: {response.Payload.GetRawText()}");

        Assert.True(result.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array,
            $"tools/list did not return tools array: {response.Payload.GetRawText()}");

        return tools
            .EnumerateArray()
            .Select(item => ReadString(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private static Task SendInitializedNotificationAsync(HttpClient webwright, string sessionId, CancellationToken ct)
    {
        return SendNotificationAsync(
            webwright,
            method: "notifications/initialized",
            parameters: new { },
            sessionId,
            ct);
    }

    private static async Task<JsonElement> WaitForRunCompletionAsync(
        HttpClient webwright,
        string sessionId,
        string runId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        JsonElement? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var runState = await CallToolAsync(
                webwright,
                sessionId,
                "browser_get_run",
                new { runId },
                ct);

            last = runState;
            var status = ReadString(runState, "status");
            if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return runState;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        Assert.Fail($"Run '{runId}' did not complete within timeout. Last state: {last?.GetRawText() ?? "<none>"}");
        return default;
    }

    private static async Task<JsonElement> CallToolAsync(
        HttpClient webwright,
        string sessionId,
        string toolName,
        object args,
        CancellationToken ct)
    {
        var response = await SendRpcAsync(
            webwright,
            "tools/call",
            new
            {
                name = toolName,
                arguments = args
            },
            sessionId,
            ct);

        Assert.True(response.Payload.TryGetProperty("result", out var result),
            $"tools/call '{toolName}' did not return result payload: {response.Payload.GetRawText()}");

        return UnwrapToolResult(result);
    }

    private static async Task DeleteSessionAsync(HttpClient webwright, string sessionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/mcp");
        request.Headers.TryAddWithoutValidation("mcp-session-id", sessionId);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        using var response = await webwright.SendAsync(request, ct);
        Assert.True(response.IsSuccessStatusCode,
            $"Session delete failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static async Task SendNotificationAsync(
        HttpClient webwright,
        string method,
        object? parameters,
        string sessionId,
        CancellationToken ct)
    {
        var requestPayload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters is not null)
        {
            requestPayload["params"] = parameters;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(requestPayload)
        };

        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("mcp-session-id", sessionId);

        using var response = await webwright.SendAsync(request, ct);
        Assert.True(
            response.IsSuccessStatusCode,
            $"Webwright MCP notification '{method}' failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private static async Task<RpcResponse> SendRpcAsync(
        HttpClient webwright,
        string method,
        object? parameters,
        string? sessionId,
        CancellationToken ct)
    {
        var requestPayload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["method"] = method
        };

        if (parameters is not null)
        {
            requestPayload["params"] = parameters;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(requestPayload)
        };

        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.TryAddWithoutValidation("mcp-session-id", sessionId);
        }

        using var response = await webwright.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.IsSuccessStatusCode,
            $"Webwright MCP method '{method}' failed with {(int)response.StatusCode}: {body}");

        var root = ParseRpcResponse(body);

        if (root.TryGetProperty("error", out var error))
        {
            Assert.Fail($"Webwright MCP method '{method}' returned error: {error.GetRawText()}");
        }

        var returnedSessionId = response.Headers.TryGetValues("mcp-session-id", out var sessionValues)
            ? sessionValues.FirstOrDefault()
            : null;

        return new RpcResponse(root, returnedSessionId);
    }

    private static JsonElement ParseRpcResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            Assert.Fail("Webwright MCP returned an empty response body.");
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
            Assert.Fail($"Webwright MCP returned non-JSON/non-SSE body: {body}");
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
                if (!string.Equals(ReadString(item, "type"), "text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = ReadString(item, "text");
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

    private static string ReadString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private readonly record struct RpcResponse(JsonElement Payload, string? SessionId);

    private sealed class DockerWebwrightE2eConfig
    {
        public const string EnabledEnvVar = "LEANKERNEL_DOCKER_E2E_ENABLED";

        public bool Enabled { get; }

        public string WebwrightBaseUrl { get; }

        public TimeSpan RunTimeout { get; }

        public static DockerWebwrightE2eConfig FromEnvironment()
        {
            var enabledRaw = Environment.GetEnvironmentVariable(EnabledEnvVar);
            var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase);

            var webwrightBaseUrl =
                Environment.GetEnvironmentVariable("LEANKERNEL_E2E_WEBWRIGHT_URL")
                ?? "http://localhost:8000";

            var runTimeoutSecondsRaw = Environment.GetEnvironmentVariable("LEANKERNEL_E2E_WEBWRIGHT_RUN_TIMEOUT_SECONDS");
            var runTimeoutSeconds = int.TryParse(runTimeoutSecondsRaw, out var value) && value > 0
                ? value
                : 90;

            return new DockerWebwrightE2eConfig(
                enabled,
                webwrightBaseUrl,
                TimeSpan.FromSeconds(runTimeoutSeconds));
        }

        public async Task ValidatePreflightAsync(CancellationToken ct)
        {
            using var webwright = BuildHttpClient(WebwrightBaseUrl);

            Exception? lastFailure = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    using (var ping = await webwright.GetAsync("/ping", ct))
                    {
                        Assert.True(
                            ping.IsSuccessStatusCode,
                            $"Webwright health check failed: {(int)ping.StatusCode}");
                    }

                    var sessionId = await InitializeSessionAsync(webwright, ct);
                    await SendInitializedNotificationAsync(webwright, sessionId, ct);
                    await DeleteSessionAsync(webwright, sessionId, ct);
                    return;
                }
                catch (Exception ex) when (attempt < 5)
                {
                    lastFailure = ex;
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }

            Assert.Fail($"Webwright preflight failed after retries: {lastFailure?.Message ?? "unknown error"}");
        }

        private DockerWebwrightE2eConfig(bool enabled, string webwrightBaseUrl, TimeSpan runTimeout)
        {
            Enabled = enabled;
            WebwrightBaseUrl = webwrightBaseUrl;
            RunTimeout = runTimeout;
        }
    }
}