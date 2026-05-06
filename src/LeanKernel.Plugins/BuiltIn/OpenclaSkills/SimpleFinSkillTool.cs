using System.Diagnostics;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// SimpleFin Bridge CLI wrapper — inspect accounts and transactions
/// from SimpleFin Bridge integration.
/// </summary>
[ToolMetadata(
    Name = "simplefin_skill",
    Description = "SimpleFin Bridge CLI: inspect accounts and transaction history from linked financial institutions. Use for balances, transaction history, and account-level cashflow analysis.",
    Category = ToolCategory.General)]
public sealed class SimpleFinSkillTool : ITool
{
    public string Name => "simplefin_skill";
    public string Description => "Query SimpleFin Bridge via CLI.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "operation": { 
              "type": "string", 
              "description": "Operation: status, accounts, transactions, balance, cashflow",
              "enum": ["status", "accounts", "transactions", "balance", "cashflow"]
            },
            "account_id": { "type": "string", "description": "Account ID for filtered queries" },
            "days": { "type": "integer", "description": "Number of days for cashflow analysis" },
            "format": { "type": "string", "description": "Output format: json or csv" }
          },
          "required": ["operation"]
        }
        """;

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString() ?? "";

            var args = operation switch
            {
                "status" => "status",
                "accounts" => "accounts list",
                "transactions" => BuildTransactionArgs(root),
                "balance" => "accounts balance",
                "cashflow" => BuildCashflowArgs(root),
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            var output = await ExecuteCliCommand("simplefin-cli", args, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = output,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private static string BuildTransactionArgs(JsonElement root)
    {
        var args = "transactions list";
        if (root.TryGetProperty("account_id", out var accountElem))
            args += $" --account-id {accountElem.GetString()}";
        if (root.TryGetProperty("days", out var daysElem))
            args += $" --last-{daysElem.GetInt32()}d";
        return args;
    }

    private static string BuildCashflowArgs(JsonElement root)
    {
        var args = "cashflow";
        if (root.TryGetProperty("account_id", out var accountElem))
            args += $" --account-id {accountElem.GetString()}";
        if (root.TryGetProperty("days", out var daysElem))
            args += $" --last-{daysElem.GetInt32()}d";
        return args;
    }

    private static async Task<string> ExecuteCliCommand(string command, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                return $"Command failed with exit code {process.ExitCode}: {error}";
            }

            return await process.StandardOutput.ReadToEndAsync();
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return "Command execution timed out";
        }
    }
}
