using LeanKernel.Plugins.BuiltIn.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

var parser = new SkillParser();
var skillPath = "/Users/achingono/source/repos/LeanKernel/.github/skills-remote/simplefin/SKILL.md";

Console.WriteLine($"File exists: {File.Exists(skillPath)}");

var skill = await parser.ParseSkillFileAsync(skillPath);

if (skill == null)
{
    Console.WriteLine("ERROR: Parser returned null");
}
else
{
    Console.WriteLine($"SUCCESS: Parsed skill '{skill.Name}'");
    Console.WriteLine($"  Runtime: {skill.Runtime}");
    if (skill.Runtime != null)
    {
        Console.WriteLine($"    Type: {skill.Runtime.Type}");
        Console.WriteLine($"    Command: {skill.Runtime.Command}");
        Console.WriteLine($"    Auth: {skill.Runtime.Auth}");
        Console.WriteLine($"    Requires: {skill.Runtime.Requires}");
        if (skill.Runtime.Requires != null)
        {
            Console.WriteLine($"      Bins: {skill.Runtime.Requires.Bins?.Count ?? 0}");
            foreach (var bin in skill.Runtime.Requires.Bins ?? [])
            {
                Console.WriteLine($"        - {bin.Name} {bin.MinVersion}");
            }
        }
    }
    Console.WriteLine($"  Operations: {skill.Operations.Count}");
    foreach (var op in skill.Operations.Take(3))
    {
        Console.WriteLine($"    - {op.Id}: {op.Summary}");
    }
    Console.WriteLine($"  Validation errors: {skill.ValidationErrors.Count}");
    foreach (var err in skill.ValidationErrors)
    {
        Console.WriteLine($"    - {err}");
    }
}
