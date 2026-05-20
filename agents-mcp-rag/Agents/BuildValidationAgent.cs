using System.Diagnostics;
using agents_mcp_rag.Infrastructure;

sealed class BuildValidationAgent : IWorkflowAgent
{
    public string Name => "BuildValidationAgent";

    public Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        string? buildTarget = FindBuildTarget(state.RepoPath);
        if (string.IsNullOrWhiteSpace(buildTarget))
        {
            return Task.FromResult(new AgentResult
            {
                AgentName = Name,
                Summary = "No .sln or .csproj found for build validation.",
                ProductionBuildPassed = null,
                Findings =
                {
                    new AgentFinding
                    {
                        Severity = FindingSeverity.Medium,
                        Message = "Build validation skipped: no solution/project file detected."
                    }
                }
            });
        }

        var fullBuild = RunBuild(state.RepoPath, buildTarget);
        bool productionPassed = ValidateProductionProjects(state.RepoPath, out var productionFailures);
        var testRun = RunTests(state.RepoPath, buildTarget);

        var findings = new List<AgentFinding>(fullBuild.Findings);
        if (!productionPassed)
        {
            foreach (var productionFinding in productionFailures)
            {
                if (!findings.Any(existing => existing.Message.Equals(productionFinding.Message, StringComparison.Ordinal)))
                {
                    findings.Add(productionFinding);
                }
            }
        }

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
            ? $"Build validation passed for {NormalizePath(buildTarget)}."
            : $"Build validation failed for {NormalizePath(buildTarget)}.";

        if (fullBuild.ExitCode != 0 && productionPassed && BuildFailureClassifier.IsOnlyTestFailures(findings))
        {
            summary += " Production projects compile; remaining failures are test-project only.";
        }

        if (testRun.Ran)
        {
            summary += testRun.ExitCode == 0
                ? " All automated tests passed (dotnet test)."
                : " Automated tests failed (dotnet test).";
        }

        return Task.FromResult(new AgentResult
        {
            AgentName = Name,
            Summary = summary,
            ProductionBuildPassed = productionPassed,
            TestsPassed = testRun.Ran ? testRun.ExitCode == 0 : null,
            Findings = findings
        });
    }

    private static (bool Ran, int ExitCode, List<AgentFinding> Findings) RunTests(string repoPath, string buildTarget)
    {
        if (!HasTestProjects(repoPath))
        {
            return (false, 0, new List<AgentFinding>());
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{buildTarget}\" --nologo --no-build",
                WorkingDirectory = repoPath,
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
            .Any(path => BuildFailureClassifier.IsTestProjectPath(path));
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
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{buildTarget}\" --nologo",
                WorkingDirectory = repoPath,
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
                    Message = "Build failed; inspect build output for details."
                });
            }
        }

        return (process.ExitCode, findings);
    }

    private static bool ValidateProductionProjects(string repoPath, out List<AgentFinding> failures)
    {
        failures = new List<AgentFinding>();
        var productionProjects = Directory
            .EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => !BuildFailureClassifier.IsTestProjectPath(path))
            .OrderBy(path => path.Length)
            .ToList();

        if (productionProjects.Count == 0)
        {
            return true;
        }

        bool allPassed = true;
        foreach (string projectPath in productionProjects)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{projectPath}\" --nologo",
                    WorkingDirectory = repoPath,
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
            if (process.ExitCode != 0)
            {
                allPassed = false;
                failures.AddRange(ExtractBuildErrors(stdout, stderr));
            }
        }

        return allPassed;
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

    private static List<AgentFinding> ExtractBuildErrors(string stdout, string stderr)
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

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
