using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

/// <summary>
/// Discovered companion rules for architecture deliverables (interfaces, layer siblings, path rules).
/// Built from <see cref="RepoContract"/> and planned paths — no hard-coded folder names.
/// </summary>
internal sealed class DeliverableCompanionContext
{
    private static readonly Regex RoleSuffixFromFileRegex = new(
        @"^(?<entity>[A-Za-z][A-Za-z0-9_]*)(?<role>[A-Z][a-zA-Z0-9]{2,})$",
        RegexOptions.Compiled);

    private readonly IReadOnlyList<string> _plannedPaths;
    private readonly HashSet<string> _plannedFileNames;
    private readonly HashSet<string> _subjectBases;
    private readonly LayerConventionProfiles _layerConventions;
    private readonly RepoContract? _contract;

    private DeliverableCompanionContext(
        IReadOnlyList<string> plannedPaths,
        HashSet<string> plannedFileNames,
        HashSet<string> subjectBases,
        LayerConventionProfiles layerConventions,
        RepoContract? contract)
    {
        _plannedPaths = plannedPaths;
        _plannedFileNames = plannedFileNames;
        _subjectBases = subjectBases;
        _layerConventions = layerConventions;
        _contract = contract;
    }

    internal static DeliverableCompanionContext Create(
        RepoContract? contract,
        IReadOnlyList<string> plannedPaths)
    {
        var plannedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subjectBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LayerConventionProfiles layerConventions = contract?.LayerConventions ?? LayerConventionProfiles.Empty;

        foreach (string planned in plannedPaths)
        {
            string normalized = ArchitectureDeliverableMatcher.NormalizePath(planned);
            plannedFileNames.Add(Path.GetFileName(normalized));

            AddSubjectFromPlannedFile(Path.GetFileName(normalized), normalized, layerConventions, subjectBases);
        }

        return new DeliverableCompanionContext(
            plannedPaths,
            plannedFileNames,
            subjectBases,
            layerConventions,
            contract);
    }

    internal bool IsCompanion(string normalizedPath)
    {
        if (IsInterfaceCompanion(normalizedPath))
        {
            return true;
        }

        string fileName = Path.GetFileName(normalizedPath);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (MatchesEntityPlacement(normalizedPath, fileName) && _subjectBases.Contains(stem))
        {
            return true;
        }

        if (!TryResolveSubject(fileName, normalizedPath, out string? subject))
        {
            return false;
        }

        if (MatchesEntityPlacement(normalizedPath, fileName))
        {
            return true;
        }

        if (MatchesDiscoveredPathRule(normalizedPath, fileName))
        {
            return true;
        }

        if (MatchesLayerConventionFile(normalizedPath, fileName))
        {
            return true;
        }

        return ArchitectureDeliverableMatcher.IsNearPlannedDeliverable(normalizedPath, _plannedPaths);
    }

    private bool TryResolveSubject(string fileName, string normalizedPath, out string subject)
    {
        if (TryExtractSubject(fileName, normalizedPath, _layerConventions, _contract, out subject)
            && _subjectBases.Contains(subject))
        {
            return true;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (_subjectBases.Contains(stem))
        {
            subject = stem;
            return true;
        }

        foreach (string knownSubject in _subjectBases.OrderByDescending(value => value.Length))
        {
            if (stem.StartsWith(knownSubject, StringComparison.OrdinalIgnoreCase)
                && stem.Length > knownSubject.Length)
            {
                subject = knownSubject;
                return true;
            }
        }

        subject = string.Empty;
        return false;
    }

    private bool IsInterfaceCompanion(string normalizedPath)
    {
        string fileName = Path.GetFileName(normalizedPath);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        if (stem.StartsWith('I') && stem.Length > 1)
        {
            string implementationFile = stem[1..] + extension;
            if (_plannedFileNames.Contains(implementationFile))
            {
                return true;
            }
        }

        foreach (string plannedFileName in _plannedFileNames)
        {
            if (!plannedFileName.StartsWith('I') || plannedFileName.Length <= 1)
            {
                continue;
            }

            if (fileName.Equals(plannedFileName[1..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesEntityPlacement(string normalizedPath, string fileName)
    {
        if (_contract?.Entity is not { } entity
            || string.IsNullOrWhiteSpace(entity.CanonicalDirectory))
        {
            return false;
        }

        if (fileName.StartsWith('I'))
        {
            return false;
        }

        return ArchitectureDeliverableMatcher.PathUnderDirectory(
            normalizedPath,
            entity.CanonicalDirectory);
    }

    private bool MatchesDiscoveredPathRule(string normalizedPath, string fileName)
    {
        if (_contract?.PathRules is not { Count: > 0 } rules)
        {
            return false;
        }

        foreach (PathPlacementRule rule in rules)
        {
            if (!fileName.EndsWith(rule.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.FileFilter is not null && !rule.FileFilter(fileName))
            {
                continue;
            }

            if (ArchitectureDeliverableMatcher.PathUnderDirectory(normalizedPath, rule.Directory))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesLayerConventionFile(string normalizedPath, string fileName)
    {
        foreach (LayerConventionProfile profile in _layerConventions.GetActiveProfiles())
        {
            if (!fileName.EndsWith(profile.FileSuffix, StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith('I'))
            {
                continue;
            }

            string? subject = LayerConventionProfiles.GetSubjectBaseName(fileName, profile);
            if (string.IsNullOrWhiteSpace(subject) || !_subjectBases.Contains(subject))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(profile.CanonicalDirectory))
            {
                if (ArchitectureDeliverableMatcher.PathUnderDirectory(normalizedPath, profile.CanonicalDirectory))
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSubjectFromPlannedFile(
        string fileName,
        string normalizedPath,
        LayerConventionProfiles layerConventions,
        HashSet<string> subjectBases)
    {
        if (TryExtractSubject(fileName, normalizedPath, layerConventions, null, out string subject))
        {
            subjectBases.Add(subject);
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        Match match = RoleSuffixFromFileRegex.Match(stem);
        if (match.Success)
        {
            subjectBases.Add(match.Groups["entity"].Value);
        }
    }

    private static bool TryExtractSubject(
        string fileName,
        string normalizedPath,
        LayerConventionProfiles layerConventions,
        RepoContract? contract,
        out string subject)
    {
        subject = string.Empty;
        if (layerConventions.ResolveByPath(normalizedPath) is LayerConventionProfile pathProfile)
        {
            string? fromPath = LayerConventionProfiles.GetSubjectBaseName(fileName, pathProfile);
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                subject = fromPath;
                return true;
            }
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (LayerConventionProfile profile in layerConventions.GetActiveProfiles())
        {
            if (!stem.EndsWith(profile.RoleName, StringComparison.OrdinalIgnoreCase)
                || stem.Length <= profile.RoleName.Length)
            {
                continue;
            }

            subject = stem[..^profile.RoleName.Length];
            return true;
        }

        string testSuffix = Path.GetFileNameWithoutExtension(
            contract?.PathRules.FirstOrDefault(rule =>
                rule.FileSuffix.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))?.FileSuffix
            ?? "Tests.cs");
        if (stem.EndsWith(testSuffix, StringComparison.OrdinalIgnoreCase) && stem.Length > testSuffix.Length)
        {
            subject = stem[..^testSuffix.Length];
            return true;
        }

        return false;
    }
}
