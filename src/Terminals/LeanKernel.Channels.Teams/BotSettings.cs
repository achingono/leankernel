namespace LeanKernel.Channels.Teams;

/// <summary>Configuration settings for the Teams bot.</summary>
public sealed class BotSettings
{
    /// <summary>Gets or sets the bot application (client) ID.</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>Gets or sets the bot application password (client secret).</summary>
    public string AppPassword { get; set; } = string.Empty;
    /// <summary>Gets or sets the OAuth authority endpoint.</summary>
    public string Authority { get; set; } = "https://login.microsoftonline.com";
    /// <summary>Gets or sets the OpenID Connect metadata URL for bot authentication.</summary>
    public string OpenIdMetadataUrl { get; set; } = "https://login.botframework.com/v1/.well-known/openidconfiguration";
    /// <summary>Gets or sets the set of valid token issuers.</summary>
    public string[] ValidIssuers { get; set; } = [
        "https://api.botframework.com",
        "https://api.botframework.us"
    ];
    /// <summary>Gets or sets the allowed service URL host suffixes for outbound replies.</summary>
    public string[] AllowedServiceUrlHostSuffixes { get; set; } = [
        ".trafficmanager.net",
        ".botframework.com",
        ".botframework.us"
    ];

    /// <summary>Gets or sets the maximum number of image attachments to forward per message.</summary>
    public int MaxImageAttachmentsPerMessage { get; set; } = 3;

    /// <summary>Gets or sets the maximum number of non-image attachments to stage per message.</summary>
    public int MaxFileAttachmentsPerMessage { get; set; } = 3;

    /// <summary>Gets or sets the maximum attachment size in bytes for downloads.</summary>
    public int MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;
}