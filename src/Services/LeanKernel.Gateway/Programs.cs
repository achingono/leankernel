using System.Security.Principal;
using LeanKernel;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Identity;
using LeanKernel.Gateway.Requests;
using LeanKernel.Gateway.Sessions;
using LeanKernel.Logic;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.Configure<IdentitySettings>(builder.Configuration.GetSection("Identity"));
builder.Services.Configure<FileSettings>(builder.Configuration.GetSection("Files"));

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
        var identitySettings = builder.Configuration
            .GetSection("Identity").Get<IdentitySettings>() ?? new IdentitySettings();
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
    var connectionStringNames = new[] { "SqlServer", "Sqlite", "Postgres" };
    string? connectionString = null;
    string? connectionStringName = null;
    foreach (var name in connectionStringNames)
    {
        connectionString = builder.Configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            connectionStringName = name;
            break;
        }
    }

    if (string.IsNullOrWhiteSpace(connectionString) && builder.Environment.EnvironmentName != "Testing")
        throw new InvalidOperationException(
            $"Connection string is missing. Specify any of '{string.Join(",", connectionStringNames)}'.");

    switch (connectionStringName)
    {
        case "SqlServer":
            options.UseSqlServer(connectionString, opts => opts.EnableRetryOnFailure());
            break;
        case "Sqlite":
            options.UseSqlite(connectionString);
            break;
        case "Postgres":
            options.UseNpgsql(connectionString);
            break;
    }

    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors(true)
               .EnableSensitiveDataLogging(true);
});

// LeanKernel providers (registered against base types)
builder.Services.AddContextProviders();

// Stub memory client (replace with GBrain-backed implementation when available)
builder.Services.AddScoped<IMemoryClient, StubMemoryClient>();

// Chat client (OpenAI-compatible)
builder.Services.AddLeanKernelChatClient();

// Agent session store (durable, isolation-scoped)
builder.Services.AddScoped<DbAgentSessionStore>();
builder.Services.AddScoped<SessionIsolationKeyProvider, IdentityIsolationKeyProvider>();
builder.Services.AddScoped<AgentSessionStore>(sp =>
{
    var innerStore = sp.GetRequiredService<DbAgentSessionStore>();
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

var app = builder.Build();

// Apply pending migrations and seed required entities
if (app.Environment.EnvironmentName != "Testing")
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LeanKernel.Data.EntityContext>();
    await dbContext.Database.MigrateAsync();

    // Seed default tenant for the configured host
    var hostName = app.Configuration["WebHost:HostName"] ?? "localhost";
    if (!await dbContext.Tenants.AnyAsync(t => t.HostName == hostName))
    {
        dbContext.Tenants.Add(new LeanKernel.Entities.TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Default Tenant",
            Description = "Default tenant created at startup",
            HostName = hostName,
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new LeanKernel.Entities.Badge { Id = Guid.Empty, FullName = "System", Email = "system@leankernel.local" }
        });
        await dbContext.SaveChangesAsync();
    }

    // Seed OpenAI HTTP channel
    if (!await dbContext.Channels.AnyAsync(c => c.Name == LeanKernel.Entities.ChannelEntity.OpenAiHttpName))
    {
        dbContext.Channels.Add(new LeanKernel.Entities.ChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = LeanKernel.Entities.ChannelEntity.OpenAiHttpName
        });
        await dbContext.SaveChangesAsync();
    }
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

public partial class Program;
