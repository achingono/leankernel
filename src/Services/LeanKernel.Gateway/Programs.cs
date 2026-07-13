using System.Security.Principal;
using System.Text.Json;
using LeanKernel;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Gateway;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.HealthChecks;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using LeanKernel.Gateway.Sessions;
using LeanKernel.Logic;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Host.UseDefaultServiceProvider(options =>
    {
        options.ValidateScopes = false;
        options.ValidateOnBuild = false;
    });
}

// Bind configuration
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 2;
});

builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AgentSettings>(builder.Configuration.GetSection("Agents"));
builder.Services.Configure<MemorySettings>(builder.Configuration.GetSection("OpenAI:Memory"));
builder.Services.Configure<FactExtractionSettings>(builder.Configuration.GetSection("OpenAI:FactExtraction"));
builder.Services.Configure<IdentitySettings>(builder.Configuration.GetSection("Identity"));
builder.Services.Configure<FileSettings>(builder.Configuration.GetSection("Files"));
builder.Services.Configure<GBrainSettings>(builder.Configuration.GetSection("GBrain"));

// Request-scoped identity accessors
builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.TryAddSingleton<IPrincipalAccessor, PrincipalAccessor>();
builder.Services.TryAddScoped<IPrincipal>(provider =>
{
    var accessor = provider.GetRequiredService<IPrincipalAccessor>();
    return accessor.Principal ?? new System.Security.Principal.GenericPrincipal(
        new System.Security.Principal.GenericIdentity(string.Empty), []);
});
builder.Services.TryAddSingleton<IHostNameAccessor, HostNameAccessor>();

// Identity/permit resolution (request-scoped)
builder.Services.AddScoped<IIdentityResolver, IdentityResolver>();
builder.Services.AddScoped<IPermit, RequestContextPermit>();

// Session prerequisites for anonymous isolation fallback
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

// Authentication & authorization
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.ValidateLifetime = false;
        options.TokenValidationParameters.ValidateIssuerSigningKey = false;
        options.SaveToken = true;
    });
builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocal", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// EF Core EntityContext with interceptors resolved from real DI
builder.Services.AddEntityContext(options =>
{
    var (connectionStringName, connectionString) = builder.Configuration.ResolveConnectionString();

    options.ConfigureOptions(connectionStringName, connectionString,
        builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase),
        builder.Environment.IsDevelopment(),
        builder.Environment.IsDevelopment());
});

// LeanKernel providers (registered against base types)
builder.Services.AddContextProviders();

// Memory client: use GBrain-backed implementation when configured, otherwise stub
var gbrainSettings = builder.Configuration.GetSection("GBrain").Get<GBrainSettings>() ?? new GBrainSettings();
builder.Services.AddGBrainMemory(gbrainSettings);

// Chat client (OpenAI-compatible)
builder.Services.AddLeanKernelChatClient();

// Health checks for dependent services
builder.Services.AddGatewayHealthChecks();

// Agent session store (durable, isolation-scoped)
builder.Services.AddScoped<DbAgentStateStore>();
builder.Services.AddScoped<SessionIsolationKeyProvider, IdentityIsolationKeyProvider>();
builder.Services.AddScoped<AgentSessionStore>(sp =>
{
    var innerStore = sp.GetRequiredService<DbAgentStateStore>();
    var keyProvider = sp.GetRequiredService<SessionIsolationKeyProvider>();
    return new IsolationKeyScopedAgentSessionStore(
        innerStore,
        keyProvider,
        new IsolationKeyScopedAgentSessionStoreOptions { Strict = true });
});

// Named AI agent
builder.Services.AddLeanKernelAgent("leankernel", builder.Configuration);

// OpenAI Responses & Conversations endpoints
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevUI(options =>
    {
        options.AllowRemoteAccess = true;
    });
}

var app = builder.Build();

// Apply pending migrations and seed required entities
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<EntityContext>();
    var hostName = app.Configuration["WebHost:HostName"] ?? "localhost";
    context.ApplyMigrationsAndSeedAsync(hostName).GetAwaiter().GetResult();
}

// Apply forwarded headers before anything reads Request.Host
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseCors("AllowLocal");

// Session middleware (needed for anonymous isolation)
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.Run();

/// <summary>
/// Application entry point for the LeanKernel gateway service.
/// </summary>
#pragma warning disable S1118 // Required for WebApplicationFactory in integration tests
public partial class Program;
#pragma warning restore S1118
