using LeanKernel.Channels.Signal;
using LeanKernel.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<GatewaySettings>(builder.Configuration.GetSection("Gateway"));
builder.Services.Configure<SignalSettings>(builder.Configuration.GetSection("Signal"));

var gatewaySettings = builder.Configuration.GetSection("Gateway").Get<GatewaySettings>() ?? new GatewaySettings();
var signalSettings = builder.Configuration.GetSection("Signal").Get<SignalSettings>() ?? new SignalSettings();
var (connectionStringName, connectionStringValue) = ResolveConnectionString(builder.Configuration);

builder.Services.AddHttpClient<GatewayChannelClient>(client =>
{
    client.BaseAddress = new Uri(gatewaySettings.BaseUrl);
});
builder.Services.AddHttpClient("signal-api", client =>
{
    client.BaseAddress = new Uri($"http://{signalSettings.Host}:{signalSettings.Port}");
});
builder.Services.AddDbContextFactory<EntityContext>(options =>
{
    if (string.Equals(connectionStringName, "Postgres", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionStringValue);
        return;
    }

    if (string.Equals(connectionStringName, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionStringValue);
        return;
    }

    throw new InvalidOperationException($"Unsupported database provider '{connectionStringName}'.");
});
builder.Services.AddSingleton<ITransportClient, SocketTransportClient>();
builder.Services.AddSingleton<IChannelCredentialProvider, DatabaseChannelCredentialProvider>();
builder.Services.AddHostedService<TerminalService>();

var app = builder.Build();
await app.RunAsync();

static (string Name, string Value) ResolveConnectionString(IConfiguration configuration)
{
    foreach (var name in new[] { "Postgres", "Sqlite" })
    {
        var value = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(value))
            return (name, value);
    }

    throw new InvalidOperationException("A database connection string is required. Configure ConnectionStrings:Postgres or ConnectionStrings:Sqlite.");
}
