using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Converts a <see cref="SkillOperation"/> of a CLI-type skill into a LeanKernel
/// <see cref="ToolDefinition"/> that executes the declared binary as a child process.
/// </summary>
public static class DynamicCliTool
{
    /// <summary>
    /// Creates a tool definition from a CLI skill definition and one of its operations.
    /// The tool name is {skill.Name}_{operation.Id}.
    /// </summary>
    /// <param name="skill">The skill definition.</param>
    /// <param name="operation">The operation to bind.</param>
    /// <param name="scopeFactory">The service scope factory for creating scoped dependencies.</param>
    /// <returns>A <see cref="ToolDefinition"/> for the given skill operation.</returns>
    public static ToolDefinition Create(
        SkillDefinition skill,
        SkillOperation operation,
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        var toolName = $"{skill.Name}_{operation.Id}";
        var command = skill.Runtime.Command;

        return new ToolDefinition
        {
            Name = toolName,
            Description = $"{skill.Description}: {operation.Summary}",
            Category = skill.Category ?? "dynamic",
            Parameters = operation.Parameters
                .Select(p => new ToolParameter
                {
                    Name = p.Name,
                    Type = p.Type,
                    Description = p.Description,
                    Required = p.Required
                })
                .ToList(),
            Handler = async (args, ct) =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var maxOutputChars = scope.ServiceProvider
                        .GetRequiredService<IOptions<AgentSettings>>().Value
                        .Tools.DynamicCli.MaxOutputChars;

                    // Resolve bearer token if needed; injected as a child-process environment
                    // variable, never as a CLI argument (avoids leaking via /proc or process listings).
                    string? bearerToken = null;
                    if (string.Equals(skill.Runtime.Auth.Type, Constants.Http.Headers.Bearer, StringComparison.OrdinalIgnoreCase))
                    {
                        bearerToken = SkillSecretResolver.Resolve(skill.Runtime.Auth.SecretRef, out var secretError);
                        if (secretError is not null)
                        {
                            return new ToolResult { ToolName = toolName, Success = false, Error = secretError };
                        }
                    }

                    return await ExecuteCliAsync(
                        toolName, command, skill, operation, args, bearerToken, maxOutputChars, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new ToolResult { ToolName = toolName, Success = false, Error = ex.Message };
                }
            }
        };
    }

    private static async Task<ToolResult> ExecuteCliAsync(
        string toolName,
        string command,
        SkillDefinition skill,
        SkillOperation operation,
        IReadOnlyDictionary<string, object?> args,
        string? bearerToken,
        int maxOutputChars,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Failed(toolName, "CLI tool has no command configured.");
        }

        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Positional argv items first, then named flags derived from supplied parameters.
        foreach (var arg in operation.Argv)
        {
            psi.ArgumentList.Add(arg);
        }

        AddFlagArguments(psi, operation, args);

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            psi.Environment[SkillSecretResolver.ToChildEnvironmentKey(skill.Runtime.Auth.SecretRef!)] = bearerToken;
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return Failed(toolName, $"Failed to start CLI tool '{command}': {ex.Message}");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // Read stdout/stderr concurrently to avoid pipe deadlocks.
        var outputTask = Task.WhenAll(
            ReadStreamAsync(process.StandardOutput, stdout, ct),
            ReadStreamAsync(process.StandardError, stderr, ct));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, skill.Runtime.TimeoutSeconds)));

        var exitTask = process.WaitForExitAsync(CancellationToken.None);

        try
        {
            await Task.WhenAll(exitTask, outputTask)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hard timeout: kill the entire process tree so runaway subprocesses cannot orphan.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await outputTask.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort kill; the process tree may already be gone.
            }

            return Failed(toolName, $"CLI tool '{command}' timed out after {skill.Runtime.TimeoutSeconds}s.");
        }

        if (process.ExitCode != 0)
        {
            return Failed(toolName, $"Exit code {process.ExitCode}: {Truncate(stderr.ToString(), maxOutputChars)}");
        }

        return new ToolResult
        {
            ToolName = toolName,
            Success = true,
            Output = Truncate(stdout.ToString(), maxOutputChars)
        };
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder sb, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (!ct.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            sb.Append(buffer, 0, read);
        }
    }

    private static void AddFlagArguments(
        ProcessStartInfo psi,
        SkillOperation operation,
        IReadOnlyDictionary<string, object?> args)
    {
        foreach (var param in operation.Parameters)
        {
            if (!args.TryGetValue(param.Name, out var value) || value is null)
            {
                continue;
            }

            if (!operation.Flags.TryGetValue(param.Name, out var flag))
            {
                continue;
            }

            // A null/empty flag mapping means the parameter is passed as a bare positional argument.
            if (string.IsNullOrWhiteSpace(flag))
            {
                psi.ArgumentList.Add(SerializeScalar(value));
                continue;
            }

            // Boolean parameters pass only the flag when true and are omitted when false.
            if (string.Equals(param.Type, "boolean", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetBoolean(value) == true)
                {
                    psi.ArgumentList.Add(flag);
                }

                continue;
            }

            psi.ArgumentList.Add(flag);
            psi.ArgumentList.Add(SerializeScalar(value));
        }
    }

    private static bool? TryGetBoolean(object? value)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return element.GetBoolean();
            }

            if (element.ValueKind == JsonValueKind.String &&
                bool.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        if (value is string text && bool.TryParse(text, out var parsedText))
        {
            return parsedText;
        }

        return null;
    }

    private static string SerializeScalar(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars];

    private static ToolResult Failed(string toolName, string error) => new()
    {
        ToolName = toolName,
        Success = false,
        Error = error
    };
}
