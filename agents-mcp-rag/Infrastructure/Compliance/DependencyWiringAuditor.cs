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
        var proposedPaths = BuildWorkflowProposedPathSet(state);
        var requiredInterfaces = CollectWorkflowIntroducedInterfaces(
            WorkflowFindingRules.GetAllProposedFiles(state),
            proposedPaths);

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
            BootstrapRegistrationScope.BootstrapScope? scope = hub is not null && File.Exists(Path.Combine(repoPath, hub.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
                ? BootstrapRegistrationScope.DiscoverFromContent(
                    File.ReadAllText(Path.Combine(repoPath, hub.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                    hub.RelativePath)
                : BootstrapRegistrationScope.DiscoverPrimary(repoPath);
            string? exemplarLine = hub?.SampleLines.FirstOrDefault(l => RegistrationPairRegex.IsMatch(l));
            string suggestedLine = BootstrapRegistrationScope.BuildRegistrationLine(scope, interfaceName, exemplarLine);

            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Missing DI registration for {interfaceName}: append one line in {hubHint} (do not modify existing registrations; mirror sibling lines, e.g. {suggestedLine})."
            });
        }

        return findings;
    }

    internal static IReadOnlyList<string> ApplyMissingRegistrations(WorkflowState state)
    {
        var applied = new List<string>();
        string repoPath = state.RepoPath;
        var registeredInterfaces = CollectRegisteredInterfaces(repoPath);
        if (registeredInterfaces.Count == 0)
        {
            return applied;
        }

        var registrationHubs = FindRegistrationHubFiles(repoPath, registeredInterfaces);
        var proposedPaths = BuildWorkflowProposedPathSet(state);
        var requiredInterfaces = CollectWorkflowIntroducedInterfaces(
            WorkflowFindingRules.GetAllProposedFiles(state),
            proposedPaths);

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
                : registrationHubs.OrderByDescending(h => h.RegisteredInterfaces.Count).FirstOrDefault(h => IsTestRegistrationHub(h.RelativePath))
                  ?? registrationHubs.FirstOrDefault();

            if (hub is null)
            {
                continue;
            }

            string hubAbsolute = Path.Combine(repoPath, hub.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(hubAbsolute))
            {
                continue;
            }

            string existing = CompositionRootMerger.SanitizeBootstrapContent(File.ReadAllText(hubAbsolute));
            BootstrapRegistrationScope.BootstrapScope? scope = BootstrapRegistrationScope.DiscoverFromContent(
                existing,
                hub.RelativePath);
            if (!TryGetImplementationTypeName(interfaceName, out string implementationName)
                || !IsWorkflowIntroducedRegistrationPair(interfaceName, implementationName, proposedPaths))
            {
                continue;
            }

            string? exemplarLine = hub.SampleLines.FirstOrDefault(l => RegistrationPairRegex.IsMatch(l));
            string registrationLine = BootstrapRegistrationScope.BuildRegistrationLine(scope, interfaceName, exemplarLine);
            if (!CompositionRootMerger.TryMergeIntoExisting(
                    existing,
                    registrationLine + Environment.NewLine,
                    out string merged,
                    out _,
                    proposedPaths))
            {
                continue;
            }

            File.WriteAllText(hubAbsolute, merged);
            registeredInterfaces.Add(interfaceName);
            applied.Add($"{hub.RelativePath}: {registrationLine.Trim()}");
        }

        return applied;
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
            "- Do NOT return bootstrap/composition-root .cs files in agent output (workflow merges registration lines automatically).",
            "- Append registrations only for interface+implementation pairs both introduced in this workflow's proposed files (I{Name} + {Name}).",
            "- Never remove or replace existing registration lines in bootstrap/composition-root files.",
            "- Do not register interfaces already wired in bootstrap/composition-root files or protected by repository contracts.",
            "- PRESERVE every existing registration line exactly (factory lambdas, singleton factories, and current Add* patterns).",
            "- Each using directive must end with ';' only — never mix namespace braces into using lines."
        };

        string? scopeContext = BootstrapRegistrationScope.BuildContext(repoPath);
        if (!string.IsNullOrWhiteSpace(scopeContext))
        {
            lines.Add(scopeContext);
        }

        var testHubs = hubs.Where(h => IsTestRegistrationHub(h.RelativePath)).ToList();
        var otherHubs = hubs.Where(h => !IsTestRegistrationHub(h.RelativePath)).ToList();

        if (testHubs.Count > 0)
        {
            lines.Add("Test bootstrap exemplars (*Test*, Bootstrappers/composition-root paths) — append new pairs only; keep existing store/factory wiring unchanged:");
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
        || relativePath.Contains("CompositionRoot", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("TestFixture", StringComparison.OrdinalIgnoreCase)
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

        foreach (string iface in existingInterfaces)
        {
            if (!HasFactoryRegistration(existingContent, iface)
                || !RegistrationPairRegex.IsMatch(proposedContent))
            {
                continue;
            }

            Match proposedPair = RegistrationPairRegex.Match(proposedContent);
            if (proposedPair.Success
                && proposedPair.Groups[1].Value.Equals(iface, StringComparison.Ordinal)
                && !HasFactoryRegistration(proposedContent, iface))
            {
                reason =
                    $"Composition root must keep factory/lambda registration for {iface}; do not replace with a direct concrete type mapping.";
                return false;
            }
        }

        return true;
    }

    /// <summary>Filters lines proposed for merge — only workflow-new pairs may be added.</summary>
    internal static bool IsAllowedNewRegistrationLine(string line, IReadOnlySet<string> workflowProposedPaths)
    {
        Match match = RegistrationPairRegex.Match(line);
        if (!match.Success)
        {
            return true;
        }

        string iface = match.Groups[1].Value;
        string impl = match.Groups[2].Value;
        return IsWorkflowIntroducedRegistrationPair(iface, impl, workflowProposedPaths);
    }

    /// <summary>Removes only invalid registration lines; never strips pre-existing bootstrap wiring.</summary>
    internal static bool ShouldRemoveInvalidRegistrationLine(string line)
    {
        Match match = RegistrationPairRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string iface = match.Groups[1].Value;
        string impl = match.Groups[2].Value;
        if (PreExistingContractGuard.IsProtectedInterfaceName(iface))
        {
            return true;
        }

        return !TryGetImplementationTypeName(iface, out string expectedImpl)
               || !expectedImpl.Equals(impl, StringComparison.Ordinal);
    }

    internal static string SanitizeBootstrapRegistrations(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            if (ShouldRemoveInvalidRegistrationLine(lines[i]))
            {
                lines[i] = string.Empty;
            }
        }

        return string.Join(Environment.NewLine, lines.Where(line => line != string.Empty));
    }

    private static bool HasFactoryRegistration(string content, string interfaceName) =>
        content.Contains($"<{interfaceName}>", StringComparison.Ordinal)
        && (content.Contains("=>", StringComparison.Ordinal)
            || content.Contains("provider =>", StringComparison.OrdinalIgnoreCase)
            || content.Contains("GetRequiredService", StringComparison.Ordinal));

    private static HashSet<string> CollectWorkflowIntroducedInterfaces(
        IEnumerable<GeneratedFile> proposedFiles,
        IReadOnlySet<string> workflowProposedPaths)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in proposedFiles)
        {
            string interfaceName = Path.GetFileNameWithoutExtension(file.RelativePath);
            if (!file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || !interfaceName.StartsWith('I')
                || !TryGetImplementationTypeName(interfaceName, out string implementationName)
                || !IsWorkflowIntroducedRegistrationPair(interfaceName, implementationName, workflowProposedPaths))
            {
                continue;
            }

            required.Add(interfaceName);
        }

        return required;
    }

    private static bool IsWorkflowIntroducedRegistrationPair(
        string interfaceName,
        string implementationName,
        IReadOnlySet<string> workflowProposedPaths)
    {
        if (PreExistingContractGuard.IsProtectedInterfaceName(interfaceName))
        {
            return false;
        }

        if (!ProposedContainsType(interfaceName, workflowProposedPaths)
            || !ProposedContainsType(implementationName, workflowProposedPaths))
        {
            return false;
        }

        return interfaceName.EndsWith(implementationName, StringComparison.Ordinal);
    }

    private static bool TryGetImplementationTypeName(string interfaceName, out string implementationName)
    {
        implementationName = string.Empty;
        if (!interfaceName.StartsWith('I') || interfaceName.Length < 2)
        {
            return false;
        }

        implementationName = interfaceName[1..];
        return implementationName.Length > 0;
    }

    private static bool ProposedContainsType(string typeName, IReadOnlySet<string> workflowProposedPaths) =>
        workflowProposedPaths.Any(path =>
            Path.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.OrdinalIgnoreCase));

    private static void AppendHubLines(List<string> lines, RegistrationHub hub)
    {
        lines.Add($"- File: {hub.RelativePath}");
        foreach (var reg in hub.SampleLines.Take(10))
        {
            lines.Add($"  {reg}");
        }
    }

    private static HashSet<string> BuildWorkflowProposedPathSet(WorkflowState state) =>
        new HashSet<string>(
            WorkflowFindingRules.GetAllProposedFiles(state).Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

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
        || relativePath.Contains("CompositionRoot", StringComparison.OrdinalIgnoreCase);

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
