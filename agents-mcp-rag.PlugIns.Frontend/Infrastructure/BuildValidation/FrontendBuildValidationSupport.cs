using System.Diagnostics;
using System.Text.Json;

namespace agents_mcp_rag.Infrastructure.BuildValidation.Frontend;

/// <summary>
/// Frontend (npm) build validation — only invoked when <see cref="RepoStack.Frontend"/>.
/// </summary>
internal static class FrontendBuildValidationSupport
{
    public static AgentResult Validate(string repoPath, RepoContract contract)
    {
        string? projectRoot = FindFrontendProjectRoot(repoPath, contract);
        if (projectRoot is null)
        {
            return new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = "No package.json found for frontend build validation.",
                ProductionBuildPassed = null,
                Findings =
                {
                    new AgentFinding
                    {
                        Severity = FindingSeverity.Medium,
                        Message = "Build validation skipped: no frontend package.json detected."
                    }
                }
            };
        }

        if (!HasBuildScript(projectRoot))
        {
            return new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = "Frontend package.json has no build script; validation skipped.",
                ProductionBuildPassed = null,
                Findings =
                {
                    new AgentFinding
                    {
                        Severity = FindingSeverity.Medium,
                        Message = "Build validation skipped: frontend package.json has no build script."
                    }
                }
            };
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

    private static string? FindFrontendProjectRoot(string repoPath, RepoContract contract)
    {
        if (contract.Frontend?.WebProjectRoot is { Length: > 0 } webRoot)
        {
            string candidate = Path.Combine(repoPath, webRoot.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "package.json")))
            {
                return candidate;
            }
        }

        return Directory
            .EnumerateFiles(repoPath, "package.json", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(path))
            .OrderBy(path => path.Length)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(dir => !string.IsNullOrWhiteSpace(dir));
    }

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

    private static bool IsExcludedPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/dist/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
    }
}
