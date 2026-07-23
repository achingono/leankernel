namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for a single document library watch folder.
/// Maps a file system path to a static identity and availability scope.
/// </summary>
public sealed class WatchFolderConfiguration
{
    /// <summary>
    /// Gets or sets the file system path to watch for new files.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier assigned to ingested files.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the person identifier assigned to ingested files.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier assigned to ingested files.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier assigned to ingested files.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the availability scope for ingested documents.
    /// </summary>
    public DocumentAvailabilityScope AvailabilityScope { get; set; }

    /// <summary>
    /// Gets or sets the file pattern to monitor (e.g., "*" or "*.pdf").
    /// </summary>
    public string FilePattern { get; set; } = "*";
}
