using System.Security.Claims;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Gateway;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat", HandleChatAsync)
            .WithName("RunChatTurn")
            .WithTags("Gateway")
            .WithSummary("Run a LeanKernel chat turn.")
            .Produces<ChatResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/health", HandleHealthAsync)
            .WithName("GetGatewayHealth")
            .WithTags("Gateway")
            .WithSummary("Get gateway health and service wiring status.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        app.MapGet("/api/diagnostics/{sessionId}", HandleDiagnosticsAsync)
            .WithName("GetSessionDiagnostics")
            .WithTags("Gateway")
            .WithSummary("Get persisted diagnostics for a session.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/diagnostics/{sessionId}/context", HandleContextDiagnosticsAsync)
            .WithName("GetSessionContextDiagnostics")
            .WithTags("Gateway")
            .WithSummary("Get the persisted context assembly audit for a session turn.")
            .Produces<ContextDiagnosticsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/diagnostics/{sessionId}/budget", HandleBudgetDiagnosticsAsync)
            .WithName("GetSessionBudgetDiagnostics")
            .WithTags("Gateway")
            .WithSummary("Get persisted budget diagnostics for a session turn.")
            .Produces<BudgetDiagnosticsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/diagnostics/{sessionId}/history", HandleHistoryDiagnosticsAsync)
            .WithName("GetSessionHistoryDiagnostics")
            .WithTags("Gateway")
            .WithSummary("Get persisted history shaping diagnostics for a session turn.")
            .Produces<HistoryDiagnosticsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HandleChatAsync(
        ChatRequest request,
        IAgentRuntime runtime,
        ISessionStore sessionStore,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ValidateApiKey(httpContext))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "Message is required" });
        }

        var authenticatedUserKey = GetAuthenticatedUserKey(httpContext);
        var isAuthenticated = !string.IsNullOrWhiteSpace(authenticatedUserKey);

        var forwardedAuthOptions = httpContext.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptionsMonitor<ForwardedAuthOptions>>()
            ?.Get(ForwardedAuthHandler.SchemeName);

        var forwardedAuthEnabled = forwardedAuthOptions?.Enabled == true;
        var forwardedAuthRequiresAuth = forwardedAuthOptions?.RequireAuthenticatedUser == true;

        string senderId;
        if (isAuthenticated)
        {
            senderId = authenticatedUserKey!;
            if (!string.IsNullOrWhiteSpace(request.UserId)
                && !string.Equals(request.UserId, authenticatedUserKey, StringComparison.Ordinal))
            {
                var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "API chat request supplied UserId '{RequestUserId}' but authenticated user is '{AuthUserId}'; using authenticated identity",
                    request.UserId, authenticatedUserKey);
            }
        }
        else
        {
            // If forwarded auth is enabled and requires an authenticated identity,
            // do not accept caller-controlled request.UserId.
            if (forwardedAuthEnabled && forwardedAuthRequiresAuth)
            {
                return Results.Unauthorized();
            }

            senderId = string.IsNullOrWhiteSpace(request.UserId)
                ? "api-user"
                : request.UserId;
        }

        var channelId = string.IsNullOrWhiteSpace(request.ChannelId)
            ? "api"
            : request.ChannelId;

        string sessionId;
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            sessionId = await sessionStore.GetOrCreateSessionIdAsync(channelId, senderId, ct).ConfigureAwait(false);
        }
        else
        {
            if (isAuthenticated)
            {
                var belongs = await sessionStore.SessionBelongsToUserAsync(request.SessionId, senderId, ct).ConfigureAwait(false);
                if (!belongs)
                {
                    return Results.NotFound(new { error = "Session not found" });
                }
            }
            sessionId = request.SessionId;
        }

        var message = new LeanKernelMessage
        {
            Content = request.Message,
            SenderId = senderId,
            ChannelId = channelId,
            SessionId = sessionId,
            Metadata = request.Metadata,
        };

        var response = await runtime.RunTurnAsync(message, ct).ConfigureAwait(false);

        return Results.Ok(new ChatResponse
        {
            Response = response,
            SessionId = message.SessionId
        });
    }

    private static string? GetAuthenticatedUserKey(HttpContext httpContext)
    {
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true
            || !string.Equals(user.Identity.AuthenticationType, ForwardedAuthHandler.SchemeName, StringComparison.Ordinal))
        {
            return null;
        }

        var sub = user.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
            return sub;

        var email = user.FindFirst(ClaimTypes.Email)?.Value
                 ?? user.FindFirst("email")?.Value;
        if (!string.IsNullOrWhiteSpace(email))
            return email;

        return user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static async Task<IResult> HandleHealthAsync(IServiceProvider services, CancellationToken ct)
    {
        var runtime = services.GetService<IAgentRuntime>();
        var knowledge = services.GetService<IKnowledgeService>();
        var healthCheckService = services.GetService<HealthCheckService>();
        var providerHealthTracker = services.GetService<IProviderHealthTracker>();
        var healthReport = healthCheckService is null
            ? null
            : await healthCheckService.CheckHealthAsync(ct).ConfigureAwait(false);
        var snapshot = providerHealthTracker?.GetSnapshot();
        var status = healthReport?.Status switch
        {
            HealthStatus.Degraded => "degraded",
            HealthStatus.Unhealthy => "unhealthy",
            _ => "healthy",
        };

        var payload = new
        {
            status,
            services = new
            {
                runtime = runtime is not null ? "ok" : "missing",
                knowledge = knowledge is not null ? "ok" : "missing"
            },
            providers = snapshot?.Providers.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    status = pair.Value.State.ToString().ToLowerInvariant(),
                    description = pair.Value.Description,
                    lastError = pair.Value.LastError,
                    failures = pair.Value.ConsecutiveFailures,
                    successes = pair.Value.ConsecutiveSuccesses,
                    lastCheckedAt = pair.Value.LastCheckedAt,
                },
                StringComparer.OrdinalIgnoreCase),
            timestamp = DateTimeOffset.UtcNow
        };

        return healthReport?.Status == HealthStatus.Unhealthy
            ? Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(payload);
    }

    private static async Task<IResult> HandleDiagnosticsAsync(
        string sessionId,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ValidateApiKey(httpContext))
        {
            return Results.Unauthorized();
        }

        var diagnosticsSink = services.GetService<IDiagnosticsSink>();
        if (diagnosticsSink is null)
        {
            return Results.Ok(new { entries = Array.Empty<object>(), message = "Diagnostics sink not configured" });
        }

        var entries = await diagnosticsSink.GetEntriesAsync(sessionId, ct).ConfigureAwait(false);
        return Results.Ok(new { entries, count = entries.Count });
    }

    private static async Task<IResult> HandleContextDiagnosticsAsync(
        string sessionId,
        string? turnId,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ValidateApiKey(httpContext))
        {
            return Results.Unauthorized();
        }

        var diagnosticsService = services.GetService<IContextDiagnosticsService>();
        var response = diagnosticsService is null
            ? null
            : await diagnosticsService.GetContextDiagnosticsAsync(sessionId, turnId, ct).ConfigureAwait(false);

        return response is null
            ? Results.NotFound(new { error = CreateNotFoundMessage("Context diagnostics", sessionId, turnId) })
            : Results.Ok(response);
    }

    private static async Task<IResult> HandleBudgetDiagnosticsAsync(
        string sessionId,
        string? turnId,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ValidateApiKey(httpContext))
        {
            return Results.Unauthorized();
        }

        var diagnosticsService = services.GetService<IContextDiagnosticsService>();
        var response = diagnosticsService is null
            ? null
            : await diagnosticsService.GetBudgetDiagnosticsAsync(sessionId, turnId, ct).ConfigureAwait(false);

        return response is null
            ? Results.NotFound(new { error = CreateNotFoundMessage("Budget diagnostics", sessionId, turnId) })
            : Results.Ok(response);
    }

    private static async Task<IResult> HandleHistoryDiagnosticsAsync(
        string sessionId,
        string? turnId,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ValidateApiKey(httpContext))
        {
            return Results.Unauthorized();
        }

        var diagnosticsService = services.GetService<IContextDiagnosticsService>();
        var response = diagnosticsService is null
            ? null
            : await diagnosticsService.GetHistoryDiagnosticsAsync(sessionId, turnId, ct).ConfigureAwait(false);

        return response is null
            ? Results.NotFound(new { error = CreateNotFoundMessage("History diagnostics", sessionId, turnId) })
            : Results.Ok(response);
    }

    private static string CreateNotFoundMessage(string diagnosticName, string sessionId, string? turnId)
        => string.IsNullOrWhiteSpace(turnId)
            ? $"{diagnosticName} not found for session '{sessionId}'."
            : $"{diagnosticName} not found for session '{sessionId}' and turn '{turnId}'.";

    private static bool ValidateApiKey(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var configuredKeys = configuration
            .GetSection("LeanKernel:Gateway:ApiKeys")
            .Get<string[]>()?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray()
            ?? [];

        var singleKey = configuration.GetValue<string>("LeanKernel:Gateway:ApiKey");
        if (!string.IsNullOrWhiteSpace(singleKey))
        {
            configuredKeys = [.. configuredKeys, singleKey];
        }

        if (configuredKeys.Length == 0)
        {
            return true;
        }

        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(apiKey)
            && configuredKeys.Contains(apiKey, StringComparer.Ordinal);
    }
}
