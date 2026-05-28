using System.Diagnostics;

namespace workflowX.Infrastructure.BuildValidation.DotNet;

internal static class DotNetBuildValidationSupport
{
    public static AgentResult Validate(string repoPath)
    {
        string? buildTarget = FindBuildTarget(repoPath);
        if (string.IsNullOrWhiteSpace(buildTarget))
        {
            return new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = "No .sln or .csproj found for .NET build validation.",
                ProductionBuildPassed = null,
                Findings =
                {
                    new AgentFinding
                    {
                        Severity = FindingSeverity.Medium,
                        Message = "Build validation skipped: no .NET solution/project file detected."
                    }
                }
            };
        }

        var fullBuild = RunBuild(repoPath, buildTarget);
        bool productionPassed = fullBuild.ExitCode == 0;
        var testRun = RunTests(repoPath, buildTarget);

        var findings = new List<AgentFinding>(fullBuild.Findings);

        if (testRun.Ran && testRun.ExitCode != 0)
        {
            foreach (var testFinding in testRun.Findings)
            {
                if (!findings.Any(existing => existing.Message.Equals(testFinding.Message, StringComparison.Ordinal)))
                {
                    findings.Add(testFinding);
                }
            }
        }

        string summary = fullBuild.ExitCode == 0
            ? $".NET build validation passed for {NormalizePath(buildTarget)}."
            : $".NET build validation failed for {NormalizePath(buildTarget)}.";

        if (testRun.Ran)
        {
            summary += testRun.ExitCode == 0
                ? " All automated tests passed (dotnet test)."
                : " Automated tests failed (dotnet test).";
        }

        return new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = summary,
            ProductionBuildPassed = productionPassed,
            TestsPassed = testRun.Ran ? testRun.ExitCode == 0 : null,
            Findings = findings
        };
    }

    internal static List<AgentFinding> ExtractBuildErrors(string stdout, string stderr)
    {
        var findings = new List<AgentFinding>();
        var lines = $"{stdout}\n{stderr}"
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase))
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

    private static (bool Ran, int ExitCode, List<AgentFinding> Findings) RunTests(string repoPath, string buildTarget)
    {
        if (!HasTestProjects(repoPath))
        {
            return (false, 0, new List<AgentFinding>());
        }

        var process = StartProcess("dotnet", $"test \"{buildTarget}\" --nologo --no-build", repoPath);
        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var findings = new List<AgentFinding>();
        if (process.ExitCode != 0)
        {
            findings.AddRange(ExtractTestFailures(stdout, stderr));
            if (findings.Count == 0)
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "Automated tests failed (dotnet test); inspect test output for details."
                });
            }
        }

        return (true, process.ExitCode, findings);
    }

    private static bool HasTestProjects(string repoPath)
    {
        return Directory
            .EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Any(path => Path.GetFileName(path).Contains("Test", StringComparison.OrdinalIgnoreCase));
    }

    private static List<AgentFinding> ExtractTestFailures(string stdout, string stderr)
    {
        var findings = new List<AgentFinding>();
        var lines = $"{stdout}\n{stderr}"
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("Failed!", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Test Run Failed", StringComparison.OrdinalIgnoreCase)
                        || line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Failed ", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        foreach (var line in lines)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Test failure: {line.Trim()}"
            });
        }

        return findings;
    }

    private static (int ExitCode, List<AgentFinding> Findings) RunBuild(string repoPath, string buildTarget)
    {
        var process = StartProcess("dotnet", $"build \"{buildTarget}\" --nologo", repoPath);
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
                    Message = "Build failed; inspect build output for details."
                });
            }
        }

        return (process.ExitCode, findings);
    }

    private static string? FindBuildTarget(string repoPath)
    {
        var solutions = Directory.EnumerateFiles(repoPath, "*.sln", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .ToList();
        if (solutions.Count > 0)
        {
            return solutions[0];
        }

        var projects = Directory.EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .ToList();

        return projects.FirstOrDefault();
    }

    private static Process StartProcess(string fileName, string arguments, string workingDirectory) =>
        new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
