namespace LeanKernel.Channels.Teams.Models;

/// <summary>Represents a Teams message attachment.</summary>
public sealed class Attachment
{
    /// <summary>Gets or sets the attachment content type.</summary>
    public string? ContentType { get; set; }

    /// <summary>Gets or sets the attachment file name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the content URL of the attachment.</summary>
    public string? ContentUrl { get; set; }
}