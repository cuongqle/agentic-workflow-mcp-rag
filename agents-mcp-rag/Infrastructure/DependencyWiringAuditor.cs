using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Detects DI registration gaps for workflow-introduced interfaces only.
/// Does not flag or suggest changes to pre-existing infrastructure (e.g. IDbStore).
/// </summary>
internal static class DependencyWiringAuditor
{
    private static readonly Regex RegistrationPairRegex = new(
        @"(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\s*<\s*(I[A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegisterTypeOfRegex = new(
        @"RegisterType\s*\(\s*typeof\s*\(\s*(I[A-Za-z0-9_]+)\s*\)\s*,\s*typeof\s*\(\s*([A-Za-z0-9_]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegisterInterfaceOnlyRegex = new(
        @"(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\s*<\s*(I[A-Za-z0-9_]+)\s*>",
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
        var requiredInterfaces = CollectWorkflowIntroducedInterfaces(state, repoPath);
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
            RegistrationHub? hub = exemplarInterface is not null
                ? registrationHubs.FirstOrDefault(h => h.RegisteredInterfaces.Contains(exemplarInterface))
                : registrationHubs.OrderByDescending(h => h.RegisteredInterfaces.Count).FirstOrDefault();

            string hubHint = hub?.RelativePath ?? "an existing DI/bootstrap file in this repository";
            string suggestedLine = BuildSuggestedRegistrationLine(hub, interfaceName);

            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Missing DI registration for {interfaceName}: append one line in {hubHint} (do not modify existing registrations; mirror sibling lines, e.g. {suggestedLine})."
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

        var lines = new List<string>
        {
            "DI registration rules:",
            "- PRESERVE every existing registration line exactly (factory lambdas, AddSingleton, InMemory types).",
            "- APPEND only new lines for workflow-introduced interfaces (mirror sibling I*Repository registrations).",
            "- Never replace pre-existing infrastructure wiring (InMemory/factory/lambda) unless you are generating that interface file."
        };

        var testHubs = hubs.Where(h => IsTestRegistrationHub(h.RelativePath)).ToList();
        var otherHubs = hubs.Where(h => !IsTestRegistrationHub(h.RelativePath)).ToList();

        if (testHubs.Count > 0)
        {
            lines.Add("Test bootstrap exemplars (*Test*, Bootstrappers/HotSpot) — append repository lines; keep InMemory/factory store wiring unchanged:");
            foreach (var hub in testHubs.Take(2))
            {
                AppendHubLines(lines, hub);
            }
        }

        if (otherHubs.Count > 0)
        {
            lines.Add("App/production DI exemplars:");
            foreach (var hub in otherHubs.Take(2))
            {
                AppendHubLines(lines, hub);
            }
        }

        return string.Join('\n', lines);
    }

    internal static bool IsCompositionRootPath(string relativePath) =>
        relativePath.Contains("Bootstrappers", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("Bootstrapper", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("HotSpot", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("Startup", StringComparison.OrdinalIgnoreCase)
        || (relativePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase)
            && relativePath.Contains("Test", StringComparison.OrdinalIgnoreCase));

    internal static bool TryValidateCompositionRootPreservation(
        string existingContent,
        string proposedContent,
        out string reason)
    {
        reason = string.Empty;
        var existingInterfaces = ExtractRegisteredInterfaceNames(existingContent);
        if (existingInterfaces.Count == 0)
        {
            return true;
        }

        var proposedInterfaces = ExtractRegisteredInterfaceNames(proposedContent);
        foreach (string iface in existingInterfaces)
        {
            if (!proposedInterfaces.Contains(iface))
            {
                reason =
                    $"Composition root must keep existing registration for {iface}. Append new lines only; do not remove or replace pre-existing wiring.";
                return false;
            }
        }

        if (existingContent.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && existingContent.Contains("IDbStore", StringComparison.OrdinalIgnoreCase)
            && RegistrationPairRegex.IsMatch(proposedContent)
            && Regex.IsMatch(proposedContent, @"Add(?:Scoped|Singleton|Transient)\s*<\s*IDbStore\s*,\s*(?!InMemory)", RegexOptions.IgnoreCase))
        {
            reason =
                "Test bootstrap must not replace InMemory/factory IDbStore registration with a production DbStore/RavenDbStore type.";
            return false;
        }

        return true;
    }

    private static void AppendHubLines(List<string> lines, RegistrationHub hub)
    {
        lines.Add($"- File: {hub.RelativePath}");
        foreach (var reg in hub.SampleLines.Take(10))
        {
            lines.Add($"  {reg}");
        }
    }

    private static string BuildSuggestedRegistrationLine(RegistrationHub? hub, string interfaceName)
    {
        string implementationName = interfaceName.StartsWith('I') && interfaceName.Length > 1
            ? interfaceName[1..]
            : interfaceName;

        if (hub is not null && hub.SampleLines.Count > 0)
        {
            string? sibling = hub.SampleLines.FirstOrDefault(l => RegistrationPairRegex.IsMatch(l));
            if (sibling is not null)
            {
                Match match = RegistrationPairRegex.Match(sibling);
                string lifetime = match.Value.Contains("Singleton", StringComparison.OrdinalIgnoreCase) ? "Singleton" : "Scoped";
                return $"services.Add{lifetime}<{interfaceName}, {implementationName}>();";
            }
        }

        return $"services.AddScoped<{interfaceName}, {implementationName}>();";
    }

    private static HashSet<string> CollectWorkflowIntroducedInterfaces(WorkflowState state, string repoPath)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        var proposed = WorkflowFindingRules.GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposed.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

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
            ExtractInterfaceDependencies(file.Content, required, repoPath, proposedPaths);
        }

        return required;
    }

    private static void ExtractInterfaceDependencies(
        string content,
        HashSet<string> required,
        string repoPath,
        HashSet<string> proposedPaths)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ResolveRegex.Matches(content))
        {
            candidates.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(
                     content,
                     @"\b(I[A-Z][A-Za-z0-9_]*(?:Repository|Service|Controller|Handler|Provider|Manager))\b"))
        {
            candidates.Add(match.Groups[1].Value);
        }

        foreach (string iface in candidates)
        {
            if (IsWorkflowIntroducedInterface(repoPath, iface, proposedPaths))
            {
                required.Add(iface);
            }
        }
    }

    private static bool IsWorkflowIntroducedInterface(
        string repoPath,
        string interfaceName,
        HashSet<string> proposedPaths)
    {
        if (proposedPaths.Any(p =>
                Path.GetFileNameWithoutExtension(p).Equals(interfaceName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!interfaceName.EndsWith("Repository", StringComparison.Ordinal))
        {
            return false;
        }

        string implName = interfaceName.StartsWith('I') ? interfaceName[1..] : interfaceName;
        return proposedPaths.Any(p =>
            Path.GetFileNameWithoutExtension(p).Equals(implName, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ExtractRegisteredInterfaceNames(string content)
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in RegistrationPairRegex.Matches(content))
        {
            registered.Add(match.Groups[1].Value);
        }

        foreach (Match match in RegisterTypeOfRegex.Matches(content))
        {
            registered.Add(match.Groups[1].Value);
        }

        foreach (Match match in RegisterInterfaceOnlyRegex.Matches(content))
        {
            registered.Add(match.Groups[1].Value);
        }

        return registered;
    }

    private static HashSet<string> CollectRegisteredInterfaces(string repoPath)
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerateSourceFiles(repoPath))
        {
            registered.UnionWith(ExtractRegisteredInterfaceNames(File.ReadAllText(file)));
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
                if (!IsRegistrationLine(line))
                {
                    continue;
                }

                sampleLines.Add(line.Trim());
                foreach (string iface in ExtractRegisteredInterfaceNames(line))
                {
                    ifaceInFile.Add(iface);
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

    private static bool IsRegistrationLine(string line) =>
        RegistrationPairRegex.IsMatch(line)
        || RegisterTypeOfRegex.IsMatch(line)
        || RegisterInterfaceOnlyRegex.IsMatch(line);

    private static bool IsTestRegistrationHub(string relativePath) =>
        relativePath.Contains("Test", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("Bootstrappers", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("HotSpot", StringComparison.OrdinalIgnoreCase);

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
