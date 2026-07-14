namespace LeanKernel.Gateway.Tools.BuiltIn;

/// <summary>
/// Provides filesystem safety helpers for file tools.
/// </summary>
public static class FileSystemSupport
{
    /// <summary>
    /// Resolves a candidate path within the allowed root, returning null when the
    /// resolved path would escape the root boundary.
    /// </summary>
    /// <param name="rootPath">The allowed root directory (absolute or relative).</param>
    /// <param name="subPath">The user-supplied sub-path to resolve. Null or empty means the root itself.</param>
    /// <returns>
    /// The fully resolved absolute path if it is within the root, or <c>null</c> if it escapes the root.
    /// </returns>
    public static string? ResolveWithinRoot(string rootPath, string? subPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(rootPath);

        string candidate;
        if (string.IsNullOrWhiteSpace(subPath))
        {
            candidate = root;
        }
        else
        {
            // Combine and fully resolve to eliminate any .. traversal
            candidate = Path.GetFullPath(Path.Combine(root, subPath));
        }

        // Ensure candidate is rooted at the allowed root
        if (!IsWithinRoot(root, candidate))
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is at or beneath <paramref name="root"/>.
    /// </summary>
    public static bool IsWithinRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedCandidate, Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
