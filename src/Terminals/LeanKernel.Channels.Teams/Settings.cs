namespace LeanKernel.Channels.Teams;

public sealed class GatewaySettings
{
    public string BaseUrl { get; set; } = "http://localhost:5088";
    public string Model { get; set; } = "gpt-4o-mini";
    public string AgentName { get; set; } = "leankernel";
}

public sealed class BotSettings
{
    public string AppId { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
    public string Authority { get; set; } = "https://login.microsoftonline.com";
    public string OpenIdMetadataUrl { get; set; } = "https://login.botframework.com/v1/.well-known/openidconfiguration";
    public string[] ValidIssuers { get; set; } = [
        "https://api.botframework.com",
        "https://api.botframework.us"
    ];
    public string[] AllowedServiceUrlHostSuffixes { get; set; } = [
        ".trafficmanager.net",
        ".botframework.com",
        ".botframework.us"
    ];
}
