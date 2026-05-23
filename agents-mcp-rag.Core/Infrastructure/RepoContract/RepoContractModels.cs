namespace agents_mcp_rag.Infrastructure;

public enum FrontendLayoutMode
{
    /// <summary>One host module folder; new UI artifacts live under that folder's discovered subfolders.</summary>
    HostModulePages,

    /// <summary>Each feature is a sibling folder under the modules root with its own bootstrap files.</summary>
    SiblingFeatureModules
}

public sealed record PathPlacementRule(string FileSuffix, string Directory, Func<string, bool>? FileFilter);

public sealed record EntityConvention(
    string CanonicalDirectory,
    string RequiredInterface,
    string ExemplarRelativePath,
    string? RequiredUsingLine)
{
    public bool ValidateEntityContent(string relativePath, string content, out string reason)
    {
        reason = string.Empty;
        if (!relativePath.Replace('\\', '/').StartsWith(CanonicalDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (content.Contains($": {RequiredInterface}", StringComparison.Ordinal)
            || content.Contains($": {RequiredInterface},", StringComparison.Ordinal))
        {
            return true;
        }

        reason =
            $"Entity '{relativePath}' must implement {RequiredInterface} (see exemplar {ExemplarRelativePath}).";
        return false;
    }
}

public sealed record FrontendModuleTemplate(
    string ModulesRoot,
    string WebProjectRoot,
    string ExemplarModuleName,
    FrontendLayoutMode LayoutMode,
    IReadOnlyList<string> ForbiddenRoots,
    IReadOnlyList<string> RequiredSubfolders,
    IReadOnlyList<string> AllowedRootFileNames,
    IReadOnlyList<string> ExemplarFilePaths);

public sealed class RepoContract
{
    public required string RepoPath { get; init; }
    public IReadOnlyList<PathPlacementRule> PathRules { get; init; } = Array.Empty<PathPlacementRule>();
    public FrontendModuleTemplate? Frontend { get; init; }
    public LayerConventionProfiles LayerConventions { get; init; } = LayerConventionProfiles.Empty;
    public EntityConvention? Entity { get; init; }
    public string? RepositoryInterfacesNamespace { get; init; }
    public IReadOnlyList<string> ConsumerSuffixes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompositionRootPaths { get; init; } = Array.Empty<string>();
    public RegistrationScopeConvention RegistrationScope { get; init; } = RegistrationScopeConvention.None;

    /// <summary>Discovered stacks — use <see cref="RepoStack.DotNet"/> / <see cref="RepoStack.Frontend"/> for routing.</summary>
    public RepoStack Stack => new(
        RegistrationScope.IsDiscovered
        || CompositionRootPaths.Count > 0
        || LayerConventions.GetActiveProfiles().Any(),
        Frontend is not null);

    public string ResolveCanonicalRelativePath(string relativePath, string content)
    {
        string path = relativePath.Replace('\\', '/').TrimStart('/');
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return path;
        }

        foreach (PathPlacementRule rule in PathRules.OrderByDescending(rule => rule.FileSuffix.Length))
        {
            if (!fileName.EndsWith(rule.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.FileFilter is not null && !rule.FileFilter(fileName))
            {
                continue;
            }

            if (path.StartsWith(rule.Directory + "/", StringComparison.OrdinalIgnoreCase)
                || path.Equals(rule.Directory, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return $"{rule.Directory}/{fileName}";
        }

        if (Frontend is not null)
        {
            string? remapped = RemapForbiddenFrontendPath(path);
            if (!string.IsNullOrWhiteSpace(remapped))
            {
                path = remapped;
            }

            path = NormalizeFeatureModuleRelativePath(path, content);
        }

        return path;
    }

    public bool TryValidateFeatureModulePath(string relativePath, out string reason)
    {
        reason = string.Empty;
        if (Frontend is null)
        {
            return true;
        }

        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string remainder = normalized[(Frontend.ModulesRoot.Length + 1)..];
        int slash = remainder.IndexOf('/');
        if (slash < 0)
        {
            return true;
        }

        string moduleSegment = remainder[..slash];
        string tail = remainder[(slash + 1)..];

        if (Frontend.LayoutMode == FrontendLayoutMode.HostModulePages
            && !moduleSegment.Equals(Frontend.ExemplarModuleName, StringComparison.OrdinalIgnoreCase))
        {
            reason =
                $"Frontend file '{relativePath}' must not create a sibling module '{moduleSegment}/'. "
                + $"Place files under {Frontend.ModulesRoot}/{Frontend.ExemplarModuleName}/ "
                + $"({FormatDiscoveredSubfolderHint()}).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tail) || tail.Contains('/'))
        {
            return true;
        }

        if (Frontend.AllowedRootFileNames.Any(name => name.Equals(tail, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (Frontend.LayoutMode == FrontendLayoutMode.HostModulePages)
        {
            return true;
        }

        reason =
            $"Frontend file '{relativePath}' must be under {Frontend.ModulesRoot}/<feature>/{string.Join("|", Frontend.RequiredSubfolders)}/ "
            + $"(only {string.Join(", ", Frontend.AllowedRootFileNames)} may sit at feature root).";
        return false;
    }

    public IEnumerable<AgentFinding> CollectFrontendFindings(IEnumerable<GeneratedFile> proposedFiles)
    {
        var findings = new List<AgentFinding>();
        if (Frontend is null)
        {
            return findings;
        }

        foreach (var file in proposedFiles)
        {
            string path = file.RelativePath.Replace('\\', '/');
            string? forbidden = Frontend.ForbiddenRoots.FirstOrDefault(root =>
                path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(forbidden))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Frontend file '{path}' must not be under parallel root '{forbidden}/'. "
                        + $"Use host project '{Frontend.WebProjectRoot}' and feature-modules root '{Frontend.ModulesRoot}/'."
                });
                continue;
            }

            if (!TryValidateFeatureModulePath(path, out string layoutReason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = layoutReason
                });
            }
        }

        findings.AddRange(CollectFeatureModuleScaffoldFindings(proposedFiles));
        return findings;
    }

    public IEnumerable<AgentFinding> CollectFeatureModuleScaffoldFindings(IEnumerable<GeneratedFile> proposedFiles)
    {
        var findings = new List<AgentFinding>();
        if (Frontend is null)
        {
            return findings;
        }

        if (Frontend.LayoutMode == FrontendLayoutMode.HostModulePages)
        {
            return findings;
        }

        string exemplarAbsolute = Path.Combine(
            RepoPath,
            Frontend.ModulesRoot.Replace('/', Path.DirectorySeparatorChar),
            Frontend.ExemplarModuleName);
        if (!Directory.Exists(exemplarAbsolute))
        {
            return findings;
        }

        var exemplarSubfolders = Directory.EnumerateDirectories(exemplarAbsolute)
            .Select(path => Path.GetFileName(path)!)
            .ToList();
        var exemplarRootFiles = Directory.EnumerateFiles(exemplarAbsolute)
            .Select(path => Path.GetFileName(path)!)
            .ToList();

        var proposedPaths = proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')).ToList();
        var featureNames = proposedPaths
            .Where(path => path.StartsWith(Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
            .Select(path => path[(Frontend.ModulesRoot.Length + 1)..])
            .Select(remainder =>
            {
                int slash = remainder.IndexOf('/');
                return slash > 0 ? remainder[..slash] : remainder;
            })
            .Where(name => !string.IsNullOrWhiteSpace(name)
                           && !name.Equals(Frontend.ExemplarModuleName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string feature in featureNames)
        {
            string featurePrefix = $"{Frontend.ModulesRoot}/{feature}/";
            foreach (string subfolder in exemplarSubfolders)
            {
                string subfolderAbsolute = Path.Combine(
                    RepoPath,
                    featurePrefix.Replace('/', Path.DirectorySeparatorChar),
                    subfolder);
                bool covered = proposedPaths.Any(path =>
                    path.StartsWith(featurePrefix + subfolder + "/", StringComparison.OrdinalIgnoreCase))
                    || (Directory.Exists(subfolderAbsolute)
                        && Directory.EnumerateFiles(subfolderAbsolute, "*.*", SearchOption.AllDirectories).Any());
                if (!covered)
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message =
                            $"Frontend feature '{feature}' is missing subfolder '{subfolder}/' "
                            + $"(required by exemplar '{Frontend.ExemplarModuleName}' under {Frontend.ModulesRoot})."
                    });
                }
            }

            foreach (string rootFile in exemplarRootFiles)
            {
                string expected = featurePrefix + rootFile;
                bool covered = proposedPaths.Any(path =>
                    path.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    || File.Exists(Path.Combine(RepoPath, expected.Replace('/', Path.DirectorySeparatorChar)));
                if (!covered)
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message =
                            $"Frontend feature '{feature}' is missing '{rootFile}' at feature root "
                            + $"(mirror {Frontend.ModulesRoot}/{Frontend.ExemplarModuleName}/{rootFile})."
                    });
                }
            }
        }

        return findings;
    }

    public string FormatStructureSummary()
    {
        var content = new System.Text.StringBuilder();
        content.AppendLine("Repository contract (discovered once at workflow start):");
        content.AppendLine($"- Root: {RepoPath}");
        if (Frontend is not null)
        {
            content.AppendLine($"- Frontend host: {Frontend.WebProjectRoot}");
            content.AppendLine($"- Feature modules root: {Frontend.ModulesRoot}");
            content.AppendLine($"- Exemplar feature: {Frontend.ExemplarModuleName}");
            content.AppendLine(
                $"- Frontend layout: {(Frontend.LayoutMode == FrontendLayoutMode.HostModulePages ? "pages inside host module" : "sibling feature modules")}");
            if (Frontend.RequiredSubfolders.Count > 0)
            {
                content.AppendLine($"- Feature subfolders: {string.Join(", ", Frontend.RequiredSubfolders)}");
            }
            if (Frontend.AllowedRootFileNames.Count > 0)
            {
                content.AppendLine($"- Feature root files: {string.Join(", ", Frontend.AllowedRootFileNames)}");
            }
            if (Frontend.ForbiddenRoots.Count > 0)
            {
                content.AppendLine($"- Forbidden parallel roots: {string.Join(", ", Frontend.ForbiddenRoots)}");
            }
        }

        if (Entity is not null)
        {
            content.AppendLine(
                $"- Entities: {Entity.CanonicalDirectory}/ must implement {Entity.RequiredInterface} (exemplar: {Entity.ExemplarRelativePath})");
        }

        foreach (var profile in LayerConventions.GetActiveProfiles())
        {
            string directory = string.IsNullOrWhiteSpace(profile.CanonicalDirectory)
                ? "discovered from repo"
                : profile.CanonicalDirectory;
            content.AppendLine(
                $"- Layer '{profile.RoleName}' (*{profile.FileSuffix}, {profile.SampleCount} samples, directory: {directory})");
        }

        foreach (var rule in PathRules.Take(12))
        {
            content.AppendLine($"- Place *{rule.FileSuffix} under {rule.Directory}/");
        }

        if (!string.IsNullOrWhiteSpace(RepositoryInterfacesNamespace))
        {
            content.AppendLine($"- Repository interfaces namespace: {RepositoryInterfacesNamespace}");
        }

        return content.ToString();
    }

    public string FormatAgentPreamble(string entityName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(FormatStructureSummary());
        sb.AppendLine();
        sb.AppendLine($"Target entity for this task: {entityName}");
        sb.AppendLine("Mirror exemplar artifacts in this repository for the same layer (paths and APIs from contract above).");
        if (Frontend is not null)
        {
            string feature = entityName.ToLowerInvariant();
            if (Frontend.LayoutMode == FrontendLayoutMode.HostModulePages)
            {
                sb.AppendLine(
                    $"New frontend: under {Frontend.ModulesRoot}/{Frontend.ExemplarModuleName}/ "
                    + $"({FormatDiscoveredSubfolderHint()}). "
                    + $"Update root bootstrap files when needed ({FormatDiscoveredRootFileHint()}). "
                    + $"Do not create {Frontend.ModulesRoot}/{feature}/ as a sibling module.");
            }
            else
            {
                sb.AppendLine(
                    $"New frontend: {Frontend.ModulesRoot}/{feature}/ mirroring exemplar "
                    + $"{Frontend.ExemplarModuleName} ({FormatDiscoveredSubfolderHint()}; "
                    + $"root files: {FormatDiscoveredRootFileHint()}).");
            }

            foreach (string exemplarPath in Frontend.ExemplarFilePaths.Take(24))
            {
                sb.AppendLine($"  - {exemplarPath}");
            }
        }

        return sb.ToString();
    }

    private string? RemapForbiddenFrontendPath(string relativePath)
    {
        if (Frontend is null)
        {
            return null;
        }

        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (string forbidden in Frontend.ForbiddenRoots)
        {
            if (!normalized.StartsWith(forbidden + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string suffix = normalized[forbidden.Length..].TrimStart('/');
            if (suffix.StartsWith("Application/modules/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix["Application/modules/".Length..];
            }
            else if (suffix.StartsWith("modules/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix["modules/".Length..];
            }

            return $"{Frontend.ModulesRoot}/{suffix}";
        }

        return null;
    }

    private string NormalizeFeatureModuleRelativePath(string relativePath, string content)
    {
        if (Frontend is null)
        {
            return relativePath;
        }

        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        normalized = RemapSiblingFeatureToHostModule(normalized);
        if (!normalized.StartsWith(Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string remainder = normalized[(Frontend.ModulesRoot.Length + 1)..];
        int slash = remainder.IndexOf('/');
        if (slash < 0)
        {
            return normalized;
        }

        string feature = remainder[..slash];
        string tail = remainder[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(tail) || tail.Contains('/'))
        {
            return normalized;
        }

        if (Frontend.AllowedRootFileNames.Any(name => name.Equals(tail, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        string? subfolder = ClassifyFrontendFeatureFile(tail, content);
        return string.IsNullOrWhiteSpace(subfolder)
            ? normalized
            : $"{Frontend.ModulesRoot}/{feature}/{subfolder}/{tail}";
    }

    private string? ClassifyFrontendFeatureFile(string fileName, string content)
    {
        if (Frontend is null)
        {
            return null;
        }

        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return PickSubfolder("views");
        }

        if (fileName.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
        {
            return PickSubfolder("proxies") ?? PickSubfolder("services");
        }

        if (fileName.Contains("Service", StringComparison.OrdinalIgnoreCase)
            || content.Contains(".factory(", StringComparison.Ordinal)
            || content.Contains(".service(", StringComparison.Ordinal))
        {
            return PickSubfolder("services");
        }

        if (fileName.Contains("Controller", StringComparison.OrdinalIgnoreCase)
            || content.Contains(".controller(", StringComparison.Ordinal))
        {
            return PickSubfolder("controllers");
        }

        if (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            return PickSubfolder("controllers");
        }

        return null;
    }

    private string? PickSubfolder(string preferred)
    {
        if (Frontend is null)
        {
            return null;
        }

        if (Frontend.RequiredSubfolders.Any(folder => folder.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
        {
            return preferred;
        }

        return Frontend.RequiredSubfolders.FirstOrDefault();
    }

    private string FormatDiscoveredSubfolderHint() =>
        Frontend?.RequiredSubfolders.Count > 0
            ? string.Join(", ", Frontend.RequiredSubfolders.Select(name => name + "/"))
            : "discovered subfolders";

    private string FormatDiscoveredRootFileHint() =>
        Frontend?.AllowedRootFileNames.Count > 0
            ? string.Join(", ", Frontend.AllowedRootFileNames)
            : "discovered root files";

    private string RemapSiblingFeatureToHostModule(string normalized)
    {
        if (Frontend is null || Frontend.LayoutMode != FrontendLayoutMode.HostModulePages)
        {
            return normalized;
        }

        string prefix = Frontend.ModulesRoot + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string remainder = normalized[prefix.Length..];
        int slash = remainder.IndexOf('/');
        if (slash <= 0)
        {
            return normalized;
        }

        string moduleSegment = remainder[..slash];
        if (moduleSegment.Equals(Frontend.ExemplarModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string tail = remainder[(slash + 1)..];
        return $"{Frontend.ModulesRoot}/{Frontend.ExemplarModuleName}/{tail}";
    }
}
