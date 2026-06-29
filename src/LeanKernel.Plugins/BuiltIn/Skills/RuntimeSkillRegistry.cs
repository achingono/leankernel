using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Manages the lifecycle and lookup of loaded skills.
/// </summary>
public sealed class RuntimeSkillRegistry
{
    private readonly List<string> _basePaths;
    private readonly SkillParser _parser;
    private readonly ILogger<RuntimeSkillRegistry> _logger;
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _quarantined = new();

    /// <summary>
    /// Gets the dictionary of loaded skills.
    /// </summary>
    public IReadOnlyDictionary<string, SkillDefinition> Skills => _skills;

    /// <summary>
    /// Gets the list of skill files that failed to load correctly.
    /// </summary>
    public IReadOnlyList<string> Quarantined => _quarantined;

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
        _basePaths = basePaths.ToList();
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Scans the base paths for skill files and loads them into the registry.
    /// </summary>
    public void LoadAll()
    {
        _skills.Clear();
        _quarantined.Clear();

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
                    var skill = _parser.Parse(file);
                    if (skill is null)
                    {
                        _quarantined.Add(file);
                        _logger.LogWarning("Failed to parse skill: {File}", file);
                        continue;
                    }

                    if (!Validate(skill, out var reason))
                    {
                        _quarantined.Add(file);
                        _logger.LogWarning("Skill validation failed for {File}: {Reason}", file, reason);
                        continue;
                    }

                    _skills[skill.Name] = skill;
                    _logger.LogInformation("Loaded skill: {Name} ({Type}) from {File}", skill.Name, skill.Runtime.Type, file);
                }
                catch (Exception ex)
                {
                    _quarantined.Add(file);
                    _logger.LogWarning(ex, "Error loading skill: {File}", file);
                }
            }
        }

        _logger.LogInformation("Skill registry loaded {Count} skills, {Quarantined} quarantined", _skills.Count, _quarantined.Count);
    }

    /// <summary>
    /// Retrieves a skill by its name.
    /// </summary>
    /// <param name="name">The name of the skill.</param>
    /// <returns>The skill definition, or null if not found.</returns>
    public SkillDefinition? GetSkill(string name)
    {
        _skills.TryGetValue(name, out var skill);
        return skill;
    }

    /// <summary>
    /// Validates the properties of a skill definition.
    /// </summary>
    /// <param name="skill">The skill to validate.</param>
    /// <param name="reason">The reason for validation failure, if any.</param>
    /// <returns>True if the skill is valid, otherwise false.</returns>
    private static bool Validate(SkillDefinition skill, out string reason)
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
                reason = $"Operation id is required";
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
}
