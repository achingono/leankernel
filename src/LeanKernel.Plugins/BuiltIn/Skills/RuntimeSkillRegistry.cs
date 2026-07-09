using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Manages the lifecycle and lookup of loaded skills.
/// </summary>
public sealed class RuntimeSkillRegistry
{
    private readonly object _sync = new();
    private readonly List<string> _basePaths;
    private readonly SkillParser _parser;
    private readonly ILogger<RuntimeSkillRegistry> _logger;
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _quarantined = new();

    /// <summary>
    /// Gets the dictionary of loaded skills.
    /// </summary>
    public IReadOnlyDictionary<string, SkillDefinition> Skills
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, SkillDefinition>(_skills, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Gets the list of skill files that failed to load correctly.
    /// </summary>
    public IReadOnlyList<string> Quarantined
    {
        get
        {
            lock (_sync)
            {
                return [.. _quarantined];
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref:RuntimeSkillRegistry/> class.
    /// </summary>
    /// <param name="basePaths">The base paths to scan for skills.</param>
    /// <param name="parser">The parser used to parse skill files.</param>
    /// <param name="logger">The logger.</param>
    public RuntimeSkillRegistry(
        IEnumerable<string> basePaths,
        SkillParser parser,
        ILogger<RuntimeSkillRegistry> logger)
    {
        _basePaths = basePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Scans the base paths for skill files and loads them into the registry.
    /// </summary>
    public void LoadAll()
    {
        lock (_sync)
        {
            _skills.Clear();
            _quarantined.Clear();
        }

        foreach (var basePath in _basePaths)
        {
            if (!Directory.Exists(basePath))
            {
                _logger.LogWarning("Skills base path does not exist: {Path}", basePath);
                continue;
            }

            var files = Directory.GetFiles(basePath, "SKILL.md", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    if (!TryGetCanonicalSkillPath(basePath, file, out var canonicalPath, out var canonicalizationReason))
                    {
                        AddQuarantined(file);
                        _logger.LogWarning("Skill file {File} was quarantined: {Reason}", file, canonicalizationReason);
                        continue;
                    }

                    var skill = _parser.Parse(file);
                    if (skill is null)
                    {
                        AddQuarantined(file);
                        _logger.LogWarning("Failed to parse skill: {File}", file);
                        continue;
                    }

                    if (!Validate(skill, out var reason))
                    {
                        AddQuarantined(file);
                        _logger.LogWarning("Skill validation failed for {File}: {Reason}", file, reason);
                        continue;
                    }

                    lock (_sync)
                    {
                        _skills[skill.Name] = skill with { SourcePath = canonicalPath };
                    }

                    _logger.LogInformation("Loaded skill: {Name} ({Type}) from {File}", skill.Name, skill.Runtime.Type, file);
                }
                catch (Exception ex)
                {
                    AddQuarantined(file);
                    _logger.LogWarning(ex, "Error loading skill: {File}", file);
                }
            }
        }

        var (loadedCount, quarantinedCount) = GetCounts();
        _logger.LogInformation("Skill registry loaded {Count} skills, {Quarantined} quarantined", loadedCount, quarantinedCount);
    }

    /// <summary>
    /// Retrieves a skill by its name.
    /// </summary>
    /// <param name="name">The name of the skill.</param>
    /// <returns>The skill definition, or null if not found.</returns>
    public SkillDefinition? GetSkill(string name)
    {
        lock (_sync)
        {
            _skills.TryGetValue(name, out var skill);
            return skill;
        }

    }

    private static bool TryGetCanonicalSkillPath(string basePath, string filePath, out string canonicalPath, out string reason)
    {
        var resolvedBasePath = Path.GetFullPath(basePath);
        var resolvedFilePath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(resolvedBasePath, resolvedFilePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            canonicalPath = string.Empty;
            reason = "skill path resolves outside configured base paths";
            return false;
        }

        canonicalPath = resolvedFilePath;
        reason = string.Empty;
        return true;
    }

    private static bool IsExecutableAvailable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(path, executable);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateRequiredBins(SkillDefinition skill, out string reason)
    {
        foreach (var bin in skill.Runtime.Requires.Bins)
        {
            if (string.IsNullOrWhiteSpace(bin.Name))
            {
                reason = "runtime.requires.bins entries must have a name";
                return false;
            }

            if (!IsExecutableAvailable(bin.Name))
            {
                reason = $"required runtime binary '{bin.Name}' is not available on PATH";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool Validate(SkillDefinition skill, out string reason)
    {
        if (!ValidateCore(skill, out reason))
        {
            return false;
        }

        if (!ValidateRequiredBins(skill, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateCore(SkillDefinition skill, out string reason)
    {
        if (string.IsNullOrWhiteSpace(skill.Name))
        {
            reason = "Name is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(skill.Description))
        {
            reason = "Description is required";
            return false;
        }

        if (skill.Operations.Count == 0)
        {
            reason = "At least one operation is required";
            return false;
        }

        foreach (var op in skill.Operations)
        {
            if (string.IsNullOrWhiteSpace(op.Id))
            {
                reason = "Operation id is required";
                return false;
            }
        }

        if (skill.Runtime.Type == "http" && skill.Runtime.Egress.AllowHosts.Count == 0)
        {
            reason = "HTTP skills require at least one egress allowHost entry";
            return false;
        }

        if (skill.Runtime.Type is "cli" or "composite" && string.IsNullOrWhiteSpace(skill.Runtime.Command))
        {
            reason = "CLI/composite skills require a command";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void AddQuarantined(string path)
    {
        lock (_sync)
        {
            _quarantined.Add(path);
        }

    }

    private (int Skills, int Quarantined) GetCounts()
    {
        lock (_sync)
        {
            return (_skills.Count, _quarantined.Count);
        }
    }
}
