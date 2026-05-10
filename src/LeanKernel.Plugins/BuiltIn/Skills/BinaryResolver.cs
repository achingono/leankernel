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

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryResolver" /> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public BinaryResolver(ILogger<BinaryResolver> logger)
    {
        _logger = logger;
        LoadManifest();
    }

    /// <summary>
    /// Executes the resolve binary operation.
    /// </summary>
    /// <param name="binaryName">The binary name.</param>
    /// <param name="minVersion">The min version.</param>
    /// <returns>The operation result.</returns>
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

    /// <summary>
    /// Executes the is binary available operation.
    /// </summary>
    /// <param name="binaryName">The binary name.</param>
    /// <param name="minVersion">The min version.</param>
    /// <returns>The operation result.</returns>
    public bool IsBinaryAvailable(string binaryName, string? minVersion = null)
    {
        return ResolveBinary(binaryName, minVersion) != null;
    }

    /// <summary>
    /// Executes the get binary version operation.
    /// </summary>
    /// <param name="binaryName">The binary name.</param>
    /// <returns>The operation result.</returns>
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
                LoadTools(toolsArray);

            _logger.LogInformation("Loaded {Count} tools from manifest", _manifest.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tools manifest from {Path}", manifestPath);
        }
    }

    private void LoadTools(JsonElement toolsArray)
    {
        foreach (var toolElem in toolsArray.EnumerateArray())
        {
            if (TryCreateBinaryInfo(toolElem, out var info))
                _manifest[info.Name] = info;
        }
    }

    private static bool TryCreateBinaryInfo(JsonElement toolElem, out BinaryInfo info)
    {
        info = null!;

        if (!TryReadRequiredToolFields(toolElem, out var name, out var version, out var path, out var type))
            return false;

        info = new BinaryInfo
        {
            Name = name,
            Version = version,
            Path = path,
            Type = type,
            Note = toolElem.TryGetProperty("note", out var noteElem) ? noteElem.GetString() : null
        };
        return true;
    }

    private static bool TryReadRequiredToolFields(
        JsonElement toolElem,
        out string name,
        out string version,
        out string path,
        out string type)
    {
        name = version = path = type = string.Empty;

        if (!toolElem.TryGetProperty("name", out var nameElem) ||
            !toolElem.TryGetProperty("version", out var versionElem) ||
            !toolElem.TryGetProperty("path", out var pathElem) ||
            !toolElem.TryGetProperty("type", out var typeElem))
        {
            return false;
        }

        name = nameElem.GetString() ?? string.Empty;
        version = versionElem.GetString() ?? string.Empty;
        path = pathElem.GetString() ?? string.Empty;
        type = typeElem.GetString() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(name) &&
               !string.IsNullOrWhiteSpace(version) &&
               !string.IsNullOrWhiteSpace(path) &&
               !string.IsNullOrWhiteSpace(type);
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
