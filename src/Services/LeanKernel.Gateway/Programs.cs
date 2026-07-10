using System.Security.Principal;
using LeanKernel.Requests;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure forwarded headers to trust proxy headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 2;
});

builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.TryAddSingleton<IPrincipalAccessor, PrincipalAccessor>();
builder.Services.TryAddScoped<IPrincipal>(provider => provider.GetRequiredService<IPrincipalAccessor>().Principal!);
builder.Services.TryAddSingleton<IHostNameAccessor, HostNameAccessor>();
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
        throw new InvalidOperationException($"Connection string is missing. Specify any of '{string.Join(",", connectionStringNames)}'.");
    else
        switch (connectionStringName)
        {
            case "SqlServer":
                options.UseSqlServer(connectionString,
                    options =>
                    {
                        options.EnableRetryOnFailure();
                    });
                break;
            case "Sqlite":
                options.UseSqlite(connectionString);
                break;
            case "Postgres":
                options.UseNpgsql(connectionString);
                break;
        }

    if (builder!.Environment.IsDevelopment())
        options.EnableDetailedErrors(true)
                .EnableSensitiveDataLogging(true);
});
builder.Services.AddContextProviders();
builder.Services.AddChatHistoryProviders();

// Add chat client and OpenAI chat completions
builder.Services.AddChatClient();

// Register services for OpenAI responses and conversations (also required for DevUI)
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

// Register the main AI agent
builder.Services.AddAgent();

var app = builder.Build();
app.UseHttpsRedirection();

// Map endpoints for OpenAI responses and conversations (also required for DevUI)
app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    // Map DevUI endpoint to /devui
    app.MapDevUI();
}

app.Run();
