namespace LeanKernel.Plugins.BuiltIn;

internal static class FileSystemPolicy
{
    public static readonly string[] DefaultWriteAllowlist =
    [
        "SELF.md",
        "USER.md",
        "agents/main/AGENTS.md"
    ];

    public static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    public static string? ResolveWithinBase(string basePath, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
        return IsWithinBase(basePath, fullPath) ? fullPath : null;
    }

    public static bool IsWithinBase(string basePath, string fullPath)
    {
        var normalizedBase = EnsureTrailingSeparator(Path.GetFullPath(basePath));
        var normalizedFull = Path.GetFullPath(fullPath);
        return normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWriteAllowed(string basePath, string fullPath, IReadOnlyCollection<string> allowlist)
    {
        if (!IsWithinBase(basePath, fullPath))
            return false;

        var relativePath = Path.GetRelativePath(basePath, fullPath).Replace('\\', '/');
        return IsRelativeWriteAllowed(relativePath, allowlist);
    }

    public static bool IsRelativeWriteAllowed(string relativePath, IReadOnlyCollection<string> allowlist)
    {
        var normalizedRelative = NormalizeRelativePath(relativePath);
        foreach (var entry in allowlist)
        {
            var normalizedEntry = NormalizeRelativePath(entry);
            if (normalizedEntry.EndsWith("/", StringComparison.Ordinal))
            {
                if (normalizedRelative.StartsWith(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            if (string.Equals(normalizedRelative, normalizedEntry, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;

        return path + Path.DirectorySeparatorChar;
    }
}