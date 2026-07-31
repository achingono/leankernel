#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// 1. Get all C# files in the src directory, excluding EF migrations
string projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), "src");
var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
    .Where(file => {
        var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
        return !normalized.Contains("/Migrations/")
            && !normalized.Contains("/obj/")
            && !normalized.Contains("/bin/");
    })
    .ToArray();

// 2. Extract all string literals from code (comments are not tokenized as string literals)
var allStrings = csFiles.SelectMany(file => {
    string code = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(code);
    var root = tree.GetRoot();

    return root.DescendantTokens()
        .Where(t => t.Kind() == SyntaxKind.StringLiteralToken)
        .Select(t => t.ValueText);
});

// 3. Group and find duplicates used more than once
var duplicatedStrings = allStrings
    .GroupBy(s => s)
    .Where(g => g.Count() > 1)
    .OrderByDescending(g => g.Count());

foreach (var group in duplicatedStrings)
{
    Console.WriteLine($"Count: {group.Count()} | String: \"{group.Key}\"");
}