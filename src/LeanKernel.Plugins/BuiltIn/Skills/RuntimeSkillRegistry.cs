using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

public sealed class RuntimeSkillRegistry
{
    private readonly List<string> _basePaths;
    private readonly SkillParser _parser;
    private readonly ILogger<RuntimeSkillRegistry> _logger;
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _quarantined = new();

    public IReadOnlyDictionary<string, SkillDefinition> Skills => _skills;
    public IReadOnlyList<string> Quarantined => _quarantined;

    public RuntimeSkillRegistry(
        IEnumerable<string> basePaths,
        SkillParser parser,
        ILogger<RuntimeSkillRegistry> logger)
    {
        _basePaths = basePaths.ToList();
        _parser = parser;
        _logger = logger;
    }

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

    public SkillDefinition? GetSkill(string name)
    {
        _skills.TryGetValue(name, out var skill);
        return skill;
    }

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
