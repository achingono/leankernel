namespace LeanKernel.Gateway.Configuration;

/// <summary>
/// Configures file storage settings for the gateway.
/// </summary>
public class FileSettings
{
    /// <summary>
    /// Gets or sets the root path used for gateway-managed files.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
