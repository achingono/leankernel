using YamlDotNet.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

var skillPath = "/Users/achingono/source/repos/LeanKernel/.github/skills-remote/simplefin/SKILL.md";
var content = File.ReadAllText(skillPath);

// Extract frontmatter
var lines = content.Split('\n');
var endIdx = Array.FindIndex(lines, 1, line => line.Trim() == "---");
var frontmatter = string.Join('\n', lines[1..endIdx]);

var deserializer = new DeserializerBuilder().Build();
var data = deserializer.Deserialize<Dictionary<string, object>>(frontmatter);

// Try to get runtime the same way as ExtractDictionary
if (data.TryGetValue("runtime", out var runtimeValue))
{
    Console.WriteLine($"Found 'runtime' key");
    Console.WriteLine($"  Type of value: {runtimeValue?.GetType().FullName}");
    Console.WriteLine($"  Value is Dictionary<string, object>: {runtimeValue is Dictionary<string, object>}");
    
    if (runtimeValue is Dictionary<string, object> runtimeDict)
    {
        Console.WriteLine($"  Matched as Dictionary<string, object>!");
        foreach (var kvp in runtimeDict)
        {
            Console.WriteLine($"    {kvp.Key}: {kvp.Value?.GetType().Name}");
        }
    }
    else
    {
        Console.WriteLine($"  Did NOT match as Dictionary<string, object>");
    }
}
else
{
    Console.WriteLine("Did not find 'runtime' key!");
    Console.WriteLine($"Available keys: {string.Join(", ", data.Keys)}");
}
