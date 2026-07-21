using LeanKernel;
using LeanKernel.Channels.Common.Configuration;
using LeanKernel.Channels.Common.HealthChecks;
using LeanKernel.Channels.Signal;
using LeanKernel.Channels.Signal.HealthChecks;
using LeanKernel.Data;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<GatewaySettings>(builder.Configuration.GetSection("Gateway"));
builder.Services.Configure<SignalSettings>(builder.Configuration.GetSection("Signal"));

var gatewaySettings = builder.Configuration.GetSection("Gateway").Get<GatewaySettings>() ?? new GatewaySettings();
var signalSettings = builder.Configuration.GetSection("Signal").Get<SignalSettings>() ?? new SignalSettings();
var (connectionStringName, connectionStringValue) = builder.Configuration.ResolveConnectionString(Constants.ConnectionStrings.All);
if (string.IsNullOrWhiteSpace(connectionStringName) || string.IsNullOrWhiteSpace(connectionStringValue))
{
    throw new InvalidOperationException("A database connection string is required. Configure ConnectionStrings:Postgres or ConnectionStrings:Sqlite.");
}

builder.Services.AddHttpClient<GatewayChannelClient>(client =>
{
    client.BaseAddress = new Uri(gatewaySettings.BaseUrl);
});
builder.Services.AddHttpClient("signal-api", client =>
{
    client.BaseAddress = new Uri($"http://{signalSettings.Host}:{signalSettings.Port}");
});

var probeTimeout = TimeSpan.FromSeconds(5);
builder.Services.AddHttpClient(GatewayHealthCheck.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = probeTimeout);
builder.Services.AddHttpClient(SignalApiHealthCheck.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = probeTimeout);
builder.Services.AddDbContextFactory<EntityContext>(options =>
{
    if (string.Equals(connectionStringName, Constants.ConnectionStrings.Postgres, StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionStringValue);
        return;
    }

    if (string.Equals(connectionStringName, Constants.ConnectionStrings.Sqlite, StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionStringValue);
        return;
    }

    throw new InvalidOperationException($"Unsupported database provider '{connectionStringName}'.");
});
builder.Services.AddSingleton<ITransportClient, SocketTransportClient>();
builder.Services.AddSingleton<IChannelCredentialProvider, DatabaseChannelCredentialProvider>();
builder.Services.AddHostedService<TerminalService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<EntityContext>(Constants.Healthchecks.Database, tags: [Constants.Healthchecks.Database])
    .AddCheck<GatewayHealthCheck>(Constants.Healthchecks.Gateway, tags: [Constants.Healthchecks.Gateway])
    .AddCheck<SignalApiHealthCheck>("signal-api", tags: ["signal-api"]);

var app = builder.Build();
app.MapGet("/live", () => Results.Ok(new { status = "alive" }));
app.MapHealthChecks(Constants.Healthchecks.Path, new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(report.ToJson());
    }
});

await app.RunAsync();