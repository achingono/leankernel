using LeanKernel.Channels.Teams;
using LeanKernel.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<GatewaySettings>(builder.Configuration.GetSection("Gateway"));
builder.Services.Configure<BotSettings>(builder.Configuration.GetSection("Bot"));

var gatewaySettings = builder.Configuration.GetSection("Gateway").Get<GatewaySettings>() ?? new GatewaySettings();
var botSettings = builder.Configuration.GetSection("Bot").Get<BotSettings>() ?? new BotSettings();
var (connectionStringName, connectionStringValue) = ResolveConnectionString(builder.Configuration);

builder.Services.AddHttpClient<GatewayClient>(client =>
{
    client.BaseAddress = new Uri(gatewaySettings.BaseUrl);
});
builder.Services.AddHttpClient("teams-auth", client =>
{
    client.BaseAddress = new Uri(botSettings.Authority);
});
builder.Services.AddHttpClient("teams-connector");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = botSettings.OpenIdMetadataUrl;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = botSettings.ValidIssuers,
            ValidateAudience = true,
            ValidAudience = botSettings.AppId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddSingleton<IChannelCredentialProvider, DatabaseChannelCredentialProvider>();
builder.Services.AddSingleton<BotFrameworkTransportClient>();
builder.Services.AddSingleton<ITransportClient>(provider => provider.GetRequiredService<BotFrameworkTransportClient>());
builder.Services.AddHostedService<TerminalService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/messages", async (
    IncomingActivityDto activity,
    HttpContext httpContext,
    BotFrameworkTransportClient transport,
    CancellationToken ct) =>
{
    if (!string.Equals(activity.Type, "message", StringComparison.OrdinalIgnoreCase))
        return Results.Accepted();

    var tokenServiceUrl = httpContext.User.FindFirst("serviceurl")?.Value;
    if (!string.IsNullOrWhiteSpace(tokenServiceUrl)
        && !string.Equals(tokenServiceUrl, activity.ServiceUrl, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var inbound = new InboundActivity(
        ActivityId: activity.Id ?? string.Empty,
        SenderId: activity.From?.Id ?? string.Empty,
        ConversationId: activity.Conversation?.Id ?? string.Empty,
        ServiceUrl: tokenServiceUrl ?? activity.ServiceUrl ?? string.Empty,
        Text: activity.Text ?? string.Empty,
        BearerToken: string.Empty,
        AttachmentUrls: activity.Attachments?
            .Select(attachment => attachment.ContentUrl ?? string.Empty)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToArray()
            ?? []);

    await transport.EnqueueAsync(inbound, ct);
    return Results.Accepted();
}).RequireAuthorization();

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
