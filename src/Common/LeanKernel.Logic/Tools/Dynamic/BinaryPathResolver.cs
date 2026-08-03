namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Resolves CLI skill binary names via standard PATH lookup.
/// </summary>
public static class BinaryPathResolver
{
    /// <summary>
    /// Resolves a command name against the process PATH environment variable.
    /// Commands containing a path separator are resolved relative to the current directory.
    /// </summary>
    /// <param name="command">The binary name or path.</param>
    /// <returns>The resolved absolute binary path, or null when not found.</returns>
    public static string? Resolve(string command)
        => Resolve(command, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// Resolves a command name against the provided PATH value (testable, deterministic).
    /// </summary>
    /// <param name="command">The binary name or path.</param>
    /// <param name="pathValue">The PATH environment variable value, or null to skip lookup.</param>
    /// <returns>The resolved absolute binary path, or null when not found.</returns>
    public static string? Resolve(string command, string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(command) ? Path.GetFullPath(command) : null;
        }

        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (OperatingSystem.IsWindows())
            {
                var exeCandidate = candidate + ".exe";
                if (File.Exists(exeCandidate))
                {
                    return exeCandidate;
                }
            }
        }

        return null;
    }
}
