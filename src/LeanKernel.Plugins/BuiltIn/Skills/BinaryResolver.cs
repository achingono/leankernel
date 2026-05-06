using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Resolves binary paths for skill requirements from a manifest.
/// </summary>
public interface IBinaryResolver
{
    /// <summary>
    /// Resolve a binary name to its absolute path.
    /// Returns null if binary is not available.
    /// </summary>
    string? ResolveBinary(string binaryName, string? minVersion = null);

    /// <summary>
    /// Check if a binary with optional version constraint is available.
    /// </summary>
    bool IsBinaryAvailable(string binaryName, string? minVersion = null);

    /// <summary>
    /// Get the installed version of a binary, or null if not found.
    /// </summary>
    string? GetBinaryVersion(string binaryName);
}

/// <summary>
/// Binary resolver that reads from a tools-manifest.json.
/// </summary>
public sealed class BinaryResolver : IBinaryResolver
{
    private readonly Dictionary<string, BinaryInfo> _manifest = [];
    private readonly ILogger<BinaryResolver> _logger;

    private class BinaryInfo
    {
        public required string Name { get; set; }
        public required string Version { get; set; }
        public required string Path { get; set; }
        public required string Type { get; set; }
        public string? Note { get; set; }
    }

    public BinaryResolver(ILogger<BinaryResolver> logger)
    {
        _logger = logger;
        LoadManifest();
    }

    public string? ResolveBinary(string binaryName, string? minVersion = null)
    {
        if (_manifest.TryGetValue(binaryName, out var info))
        {
            if (minVersion != null && !IsVersionGreaterOrEqual(info.Version, minVersion))
                return null;

            return info.Path;
        }

        return null;
    }

    public bool IsBinaryAvailable(string binaryName, string? minVersion = null)
    {
        return ResolveBinary(binaryName, minVersion) != null;
    }

    public string? GetBinaryVersion(string binaryName)
    {
        if (_manifest.TryGetValue(binaryName, out var info))
            return info.Version;

        return null;
    }

    /// <summary>
    /// Load manifest from standard location.
    /// </summary>
    private void LoadManifest()
    {
        const string manifestPath = "/opt/LeanKernel/tools/tools-manifest.json";

        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("Tools manifest not found at {Path}", manifestPath);
            return;
        }

        try
        {
            var content = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("tools", out var toolsArray))
            {
                foreach (var toolElem in toolsArray.EnumerateArray())
                {
                    if (toolElem.TryGetProperty("name", out var nameElem) &&
                        toolElem.TryGetProperty("version", out var versionElem) &&
                        toolElem.TryGetProperty("path", out var pathElem) &&
                        toolElem.TryGetProperty("type", out var typeElem))
                    {
                        var name = nameElem.GetString();
                        var version = versionElem.GetString();
                        var path = pathElem.GetString();
                        var type = typeElem.GetString();

                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version) &&
                            !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(type))
                        {
                            _manifest[name] = new BinaryInfo
                            {
                                Name = name,
                                Version = version,
                                Path = path,
                                Type = type,
                                Note = toolElem.TryGetProperty("note", out var noteElem) ? noteElem.GetString() : null
                            };
                        }
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} tools from manifest", _manifest.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tools manifest from {Path}", manifestPath);
        }
    }

    /// <summary>
    /// Check if version A is >= version B (semantic versioning).
    /// Simple implementation: compares major.minor.patch components.
    /// </summary>
    private static bool IsVersionGreaterOrEqual(string? installed, string? required)
    {
        if (string.IsNullOrWhiteSpace(required))
            return true;

        if (string.IsNullOrWhiteSpace(installed))
            return false;

        // Remove 'v' prefix if present
        installed = installed.TrimStart('v');
        required = required.TrimStart('v');

        // Parse versions (simplified: only handle major.minor.patch)
        var installedParts = installed.Split('.').Take(3).Select(p => int.TryParse(p.TakeWhile(char.IsDigit).ToString(), out var n) ? n : 0).ToArray();
        var requiredParts = required.Split('.').Take(3).Select(p => int.TryParse(p.TakeWhile(char.IsDigit).ToString(), out var n) ? n : 0).ToArray();

        // Pad with zeros
        while (installedParts.Length < 3) installedParts = installedParts.Append(0).ToArray();
        while (requiredParts.Length < 3) requiredParts = requiredParts.Append(0).ToArray();

        // Compare
        for (int i = 0; i < 3; i++)
        {
            if (installedParts[i] > requiredParts[i])
                return true;
            if (installedParts[i] < requiredParts[i])
                return false;
        }

        return true; // Equal is also >= 
    }
}
