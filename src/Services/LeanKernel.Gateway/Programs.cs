namespace LeanKernel.Gateway;

using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using LeanKernel.Gateway.Sessions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;

using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Application entry point for the LeanKernel gateway service.
/// </summary>
#pragma warning disable S1118 // Required for WebApplicationFactory in integration tests
public partial class Program
{
    /// <summary>
    /// Application entry point. Builds the service collection, configures middleware,
    /// and starts the Kestrel host for the LeanKernel gateway.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to <see cref="WebApplication.CreateBuilder(string[])"/>.</param>
    [SuppressMessage("Critical Code Smell", "S3776", Justification = "Startup wiring keeps environment and security guards explicit.")]
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            builder.Host.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = false;
                options.ValidateOnBuild = false;
            });
        }

        var identityConfig = builder.Configuration.GetSection("Identity").Get<IdentitySettings>() ?? new IdentitySettings();
        var agentConfig = builder.Configuration.GetSection("Agents").Get<AgentSettings>() ?? new AgentSettings();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var trustedProxyIps = builder.Configuration
                .GetSection("Identity:TrustedProxies")
                .Get<string[]>() ?? [];

            if (trustedProxyIps.Length > 0)
            {
                options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();

                foreach (var proxyIp in trustedProxyIps)
                {
                    if (System.Net.IPAddress.TryParse(proxyIp, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                    }
                    else if (System.Net.IPNetwork.TryParse(proxyIp, out var net))
                    {
                        options.KnownIPNetworks.Add(net);
                    }
                }
            }
            else
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
        builder.Services.Configure<AgentSettings>(builder.Configuration.GetSection("Agents"));
        builder.Services.Configure<MemorySettings>(builder.Configuration.GetSection("OpenAI:Memory"));
        builder.Services.Configure<FactExtractionSettings>(builder.Configuration.GetSection("OpenAI:FactExtraction"));
        builder.Services.Configure<IdentitySettings>(builder.Configuration.GetSection("Identity"));
        builder.Services.Configure<FileSettings>(builder.Configuration.GetSection("Files"));
        builder.Services.Configure<GBrainSettings>(builder.Configuration.GetSection("GBrain"));

        builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.TryAddSingleton<IPrincipalAccessor, PrincipalAccessor>();
        builder.Services.TryAddScoped<IPrincipal>(provider =>
        {
            var accessor = provider.GetRequiredService<IPrincipalAccessor>();
            return accessor.Principal ?? new GenericPrincipal(new GenericIdentity(string.Empty), []);
        });
        builder.Services.TryAddSingleton<IHostNameAccessor, HostNameAccessor>();

        builder.Services.AddScoped<IIdentityResolver, IdentityResolver>();
        builder.Services.AddScoped<IPermit, RequestContextPermit>();

        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();

        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.RequireHttpsMetadata = identityConfig.OpenId.RequireHttpsMetadata;
                options.SaveToken = true;

                var tokenSettings = identityConfig.Token;
                var hasSigningKey = !string.IsNullOrWhiteSpace(tokenSettings.SecretKey);
                var hasIssuer = !string.IsNullOrWhiteSpace(tokenSettings.Issuer);
                var hasAudience = !string.IsNullOrWhiteSpace(tokenSettings.Audience);

                if (hasSigningKey)
                {
                    var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(tokenSettings.SecretKey));
                    options.TokenValidationParameters.IssuerSigningKey = key;
                }

                options.TokenValidationParameters.RequireSignedTokens = hasSigningKey;
                options.TokenValidationParameters.ValidateIssuerSigningKey = hasSigningKey;
                options.TokenValidationParameters.ValidateIssuer = hasIssuer;
                options.TokenValidationParameters.ValidateAudience = hasAudience;
                options.TokenValidationParameters.ValidateLifetime = hasSigningKey;

                if (hasIssuer)
                {
                    options.TokenValidationParameters.ValidIssuer = tokenSettings.Issuer;
                }

                var trustedChannelIssuers = agentConfig.Channels.TrustedIssuers
                    .Where(issuer => !string.IsNullOrWhiteSpace(issuer))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (trustedChannelIssuers.Count > 0)
                {
                    options.TokenValidationParameters.ValidIssuers = hasIssuer
                        ? trustedChannelIssuers.Append(tokenSettings.Issuer)
                        : trustedChannelIssuers;
                    options.TokenValidationParameters.ValidateIssuer = true;
                }

                if (hasAudience)
                {
                    options.TokenValidationParameters.ValidAudience = tokenSettings.Audience;
                }

                if (!string.IsNullOrWhiteSpace(identityConfig.OpenId.Authority))
                {
                    options.Authority = identityConfig.OpenId.Authority;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(identityConfig.OpenId.ClientId);
                    options.TokenValidationParameters.ValidAudience = identityConfig.OpenId.ClientId;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                }
            });

        builder.Services.AddAuthorization();
        builder.Services.AddHostedService<ChannelConfigurationValidatorHostedService>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowLocal", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        builder.Services.AddEntityContext(options =>
        {
            var (connectionStringName, connectionString) = builder.Configuration.ResolveConnectionString([
                "Postgres",
                "SqlServer",
                "Sqlite"
            ]);

            options.ConfigureOptions(
                connectionStringName,
                connectionString,
                builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase),
                builder.Environment.IsDevelopment(),
                builder.Environment.IsDevelopment());
        });

        builder.Services.AddContextProviders();
        builder.Services.AddTelemetry(builder.Configuration);

        var gbrainSettings = builder.Configuration.GetSection("GBrain").Get<GBrainSettings>() ?? new GBrainSettings();
        builder.Services.AddGBrainMemory(gbrainSettings);

        builder.Services.AddToolRegistry();
        builder.Services.AddEmbeddingClient();
        builder.Services.AddTurnPipeline();
        builder.Services.AddLeanKernelChatClient();
        builder.Services.AddGatewayHealthChecks();

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

        builder.Services.AddLeanKernelAgent("leankernel", builder.Configuration);
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

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<EntityContext>();
            var hostName = app.Configuration["WebHost:HostName"] ?? "localhost";
            context.ApplyMigrationsAndSeedAsync(hostName).GetAwaiter().GetResult();
        }

        var agentToolSettings = app.Configuration.GetSection("Agents:Tools").Get<ToolSettings>() ?? new ToolSettings();
        if (agentToolSettings.Enabled)
        {
            app.Services.RegisterToolsAsync().GetAwaiter().GetResult();
        }

        app.UseForwardedHeaders();
        app.UseHttpsRedirection();
        app.UseCors("AllowLocal");
        app.UseSession();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        app.MapOpenAIResponses();
        app.MapOpenAIConversations();
        app.MapProxiedOpenAIChatCompletions("leankernel", "/v1/internal/completions", new OpenAIChatCompletionsMapOptions
        {
            RunOptionsFactory = _ => null,
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapDevUI();
        }

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = (context, report) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                return context.Response.WriteAsync(report.ToJson());
            },
        });

        app.Run();
    }
}

#pragma warning restore S1118