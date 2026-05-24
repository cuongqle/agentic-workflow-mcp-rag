using System.Diagnostics;
using System.Text.Json;

namespace agents_mcp_rag.Infrastructure.BuildValidation.Frontend;

/// <summary>
/// Frontend (npm) build validation — only invoked when <see cref="RepoStack.Frontend"/>.
/// Runs npm only when contract discovery found <c>package.json</c> under the initial frontend layout.
/// </summary>
internal static class FrontendBuildValidationSupport
{
    public static AgentResult Validate(string repoPath, RepoContract contract)
    {
        string? npmProjectRoot = contract.Frontend?.NpmProjectRoot;
        if (string.IsNullOrWhiteSpace(npmProjectRoot))
        {
            return Skipped(
                "Frontend npm build validation skipped: no package.json under the discovered frontend layout.");
        }

        string projectRoot = Path.Combine(repoPath, npmProjectRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(Path.Combine(projectRoot, "package.json")))
        {
            return Skipped(
                $"Frontend npm build validation skipped: package.json no longer exists at {npmProjectRoot}.");
        }

        if (!HasBuildScript(projectRoot))
        {
            return Skipped(
                $"Frontend npm build validation skipped: {npmProjectRoot}/package.json has no build script.");
        }

        string relativeRoot = Path.GetRelativePath(repoPath, projectRoot).Replace('\\', '/');
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = "run build",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var findings = new List<AgentFinding>();
        if (process.ExitCode != 0)
        {
            findings.AddRange(ExtractBuildErrors(stdout, stderr));
            if (findings.Count == 0)
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"Frontend build failed for {relativeRoot}; inspect npm output for details."
                });
            }
        }

        string summary = process.ExitCode == 0
            ? $"Frontend build validation passed for {relativeRoot} (npm run build)."
            : $"Frontend build validation failed for {relativeRoot} (npm run build).";

        return new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = summary,
            ProductionBuildPassed = process.ExitCode == 0,
            TestsPassed = null,
            Findings = findings
        };
    }

    internal static List<AgentFinding> ExtractBuildErrors(string stdout, string stderr)
    {
        var findings = new List<AgentFinding>();
        var lines = $"{stdout}\n{stderr}"
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                line.Contains(" error ", StringComparison.OrdinalIgnoreCase)
                || line.Contains("ERROR in", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Failed to compile", StringComparison.OrdinalIgnoreCase)
                || line.Contains("ERR!", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Module not found", StringComparison.OrdinalIgnoreCase)
                || line.Contains("SyntaxError:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("TypeError:", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        foreach (var line in lines)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = line.Trim()
            });
        }

        return findings;
    }

    private static AgentResult Skipped(string summary) =>
        new()
        {
            AgentName = "BuildValidationAgent",
            Summary = summary,
            ProductionBuildPassed = null,
            TestsPassed = null,
            Findings = []
        };

    private static bool HasBuildScript(string projectRoot)
    {
        string packageJsonPath = Path.Combine(projectRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (document.RootElement.TryGetProperty("scripts", out JsonElement scripts)
                && scripts.TryGetProperty("build", out JsonElement buildScript))
            {
                return !string.IsNullOrWhiteSpace(buildScript.GetString());
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
