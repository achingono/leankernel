using System.Globalization;
using System.Resources;

namespace LeanKernel.Thinker.Resources;

/// <summary>
/// Provides access to localized resource text for Thinker runtime and learning services.
/// </summary>
internal static class ResourceText
{
    private static readonly ResourceManager LogResourceManager =
        new("LeanKernel.Thinker.Resources.LogMessages", typeof(ResourceText).Assembly);

    private static readonly ResourceManager ErrorResourceManager =
        new("LeanKernel.Thinker.Resources.ErrorMessages", typeof(ResourceText).Assembly);

    /// <summary>
    /// Gets a log message template by resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The resolved message template, or the key when no resource exists.</returns>
    public static string Log(string key) =>
        LogResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Gets an error message by resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The resolved error message, or the key when no resource exists.</returns>
    public static string Error(string key) =>
        ErrorResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
