using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Detects new interfaces/types that are used in the app but not registered in discovered DI/bootstrap files.
/// </summary>
internal static class DependencyWiringAuditor
{
    private static readonly Regex RegistrationPairRegex = new(
        @"(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\s*<\s*(I[A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegisterTypeOfRegex = new(
        @"RegisterType\s*\(\s*typeof\s*\(\s*(I[A-Za-z0-9_]+)\s*\)\s*,\s*typeof\s*\(\s*([A-Za-z0-9_]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ResolveRegex = new(
        @"(?:Resolve|GetRequiredService|GetService)\s*<\s*(I[A-Za-z0-9_]+)\s*>",
        RegexOptions.Compiled);

    internal static List<AgentFinding> ValidateMissingWiring(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        string repoPath = state.RepoPath;
        var registeredInterfaces = CollectRegisteredInterfaces(repoPath);
        if (registeredInterfaces.Count == 0)
        {
            return findings;
        }

        var registrationHubs = FindRegistrationHubFiles(repoPath, registeredInterfaces);
        var requiredInterfaces = CollectInterfacesRequiringWiring(state, repoPath);
        var proposedPaths = new HashSet<string>(
            WorkflowFindingRules.GetAllProposedFiles(state).Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (string interfaceName in requiredInterfaces)
        {
            if (registeredInterfaces.Contains(interfaceName))
            {
                continue;
            }

            if (!InterfaceOrImplementationExists(repoPath, interfaceName, proposedPaths))
            {
                continue;
            }

            string? exemplarInterface = FindRegisteredExemplar(interfaceName, registeredInterfaces);
            string? hubFile = exemplarInterface is not null
                ? registrationHubs.FirstOrDefault(h => h.RegisteredInterfaces.Contains(exemplarInterface))?.RelativePath
                : registrationHubs.OrderByDescending(h => h.RegisteredInterfaces.Count).FirstOrDefault()?.RelativePath;

            string implementationName = interfaceName.StartsWith('I') && interfaceName.Length > 1
                ? interfaceName[1..]
                : interfaceName;

            string suggestedLine = $"services.AddScoped<{interfaceName}, {implementationName}>();";
            string hubHint = string.IsNullOrWhiteSpace(hubFile)
                ? "an existing DI/bootstrap file in this repository"
                : hubFile;

            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Missing DI registration for {interfaceName}: register it in {hubHint} (mirror sibling registrations, e.g. {suggestedLine})."
            });
        }

        return findings;
    }

    internal static string? BuildRegistrationContext(string repoPath)
    {
        var hubs = FindRegistrationHubFiles(repoPath, CollectRegisteredInterfaces(repoPath));
        if (hubs.Count == 0)
        {
            return null;
        }

        var lines = new List<string> { "DI registration exemplars (mirror when adding new interfaces):" };
        foreach (var hub in hubs.Take(3))
        {
            lines.Add($"- File: {hub.RelativePath}");
            foreach (var reg in hub.SampleLines.Take(8))
            {
                lines.Add($"  {reg}");
            }
        }

        return string.Join('\n', lines);
    }

    private static HashSet<string> CollectRegisteredInterfaces(string repoPath)
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerateSourceFiles(repoPath))
        {
            string content = File.ReadAllText(file);
            foreach (Match match in RegistrationPairRegex.Matches(content))
            {
                registered.Add(match.Groups[1].Value);
            }

            foreach (Match match in RegisterTypeOfRegex.Matches(content))
            {
                registered.Add(match.Groups[1].Value);
            }
        }

        return registered;
    }

    private static List<RegistrationHub> FindRegistrationHubFiles(
        string repoPath,
        HashSet<string> registeredInterfaces)
    {
        var hubs = new List<RegistrationHub>();
        foreach (var file in EnumerateSourceFiles(repoPath))
        {
            string content = File.ReadAllText(file);
            var ifaceInFile = new HashSet<string>(StringComparer.Ordinal);
            var sampleLines = new List<string>();

            foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (!RegistrationPairRegex.IsMatch(line) && !RegisterTypeOfRegex.IsMatch(line))
                {
                    continue;
                }

                sampleLines.Add(line.Trim());
                foreach (Match match in RegistrationPairRegex.Matches(line))
                {
                    ifaceInFile.Add(match.Groups[1].Value);
                }

                foreach (Match match in RegisterTypeOfRegex.Matches(line))
                {
                    ifaceInFile.Add(match.Groups[1].Value);
                }
            }

            if (ifaceInFile.Count == 0)
            {
                continue;
            }

            hubs.Add(new RegistrationHub(
                Path.GetRelativePath(repoPath, file).Replace('\\', '/'),
                ifaceInFile,
                sampleLines));
        }

        return hubs.OrderByDescending(h => h.RegisteredInterfaces.Count).ToList();
    }

    private static HashSet<string> CollectInterfacesRequiringWiring(WorkflowState state, string repoPath)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        var proposed = WorkflowFindingRules.GetAllProposedFiles(state);

        foreach (var file in proposed)
        {
            string name = Path.GetFileName(file.RelativePath);
            if (name.StartsWith('I') && name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                required.Add(Path.GetFileNameWithoutExtension(name));
            }
        }

        foreach (var file in proposed)
        {
            ExtractInterfaceDependencies(file.Content, required);
        }

        foreach (var file in EnumerateSourceFiles(repoPath))
        {
            string relative = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
            if (!IsLikelyConsumerFile(relative))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            ExtractInterfaceDependencies(content, required);
        }

        return required;
    }

    private static void ExtractInterfaceDependencies(string content, HashSet<string> required)
    {
        foreach (Match match in ResolveRegex.Matches(content))
        {
            required.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(
                     content,
                     @"\b(I[A-Z][A-Za-z0-9_]*(?:Repository|Service|Controller|Handler|Provider|Manager|Store))\b"))
        {
            required.Add(match.Groups[1].Value);
        }
    }

    private static bool IsLikelyConsumerFile(string relativePath)
    {
        return relativePath.Contains("Controller", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("Tests", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("Bootstrappers", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("Startup", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool InterfaceOrImplementationExists(
        string repoPath,
        string interfaceName,
        HashSet<string> proposedPaths)
    {
        string implName = interfaceName.StartsWith('I') ? interfaceName[1..] : interfaceName;
        return File.Exists(FindFile(repoPath, $"{interfaceName}.cs"))
               || File.Exists(FindFile(repoPath, $"{implName}.cs"))
               || proposedPaths.Any(p => Path.GetFileNameWithoutExtension(p).Equals(interfaceName, StringComparison.OrdinalIgnoreCase)
                                      || Path.GetFileNameWithoutExtension(p).Equals(implName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFile(string repoPath, string fileName)
    {
        return Directory
            .EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindRegisteredExemplar(string interfaceName, HashSet<string> registered)
    {
        string suffix = interfaceName.StartsWith('I') ? interfaceName[1..] : interfaceName;
        int layerStart = -1;
        for (int i = 1; i < suffix.Length; i++)
        {
            if (char.IsUpper(suffix[i]))
            {
                layerStart = i;
                break;
            }
        }

        if (layerStart < 0)
        {
            return registered.FirstOrDefault();
        }

        string layerSuffix = suffix[layerStart..];
        return registered.FirstOrDefault(r =>
            r.StartsWith('I') && r.EndsWith(layerSuffix, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string repoPath)
    {
        return Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RegistrationHub(
        string RelativePath,
        HashSet<string> RegisteredInterfaces,
        List<string> SampleLines);
}
