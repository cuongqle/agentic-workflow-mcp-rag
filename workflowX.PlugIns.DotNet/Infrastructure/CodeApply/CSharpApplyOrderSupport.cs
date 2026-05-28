namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Discovered layer- and contract-aware apply ordering for generated C# files.
/// </summary>
internal static class CSharpApplyOrderSupport
{
    private const int InterfacePriority = 0;
    private const int EntityPriority = 1;
    private const int AuxiliaryPriority = 2;
    private const int LayerBasePriority = 10;
    private const int DefaultPriority = 500;
    private const int TestPriority = 1_000;

    private static readonly string[] KnownRoleApplyOrder =
    [
        "Repository",
        "Service",
        "Handler",
        "Manager",
        "Controller",
        "Presenter"
    ];

    private static readonly string[] AuxiliaryFileSuffixes =
    [
        "Index.cs",
        "Expression.cs"
    ];

    internal static List<GeneratedFile> OrderForApply(
        IReadOnlyList<GeneratedFile> files,
        LayerConventionProfiles layerConventions,
        RepoContract? contract = null)
    {
        IReadOnlyDictionary<string, int> roleOrder = BuildRoleApplyOrder(layerConventions);
        return files
            .OrderBy(f => GetApplyPriority(f.RelativePath, layerConventions, roleOrder, contract))
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static int GetApplyPriority(
        string relativePath,
        LayerConventionProfiles layerConventions,
        RepoContract? contract = null)
    {
        IReadOnlyDictionary<string, int> roleOrder = BuildRoleApplyOrder(layerConventions);
        return GetApplyPriority(relativePath, layerConventions, roleOrder, contract);
    }

    private static int GetApplyPriority(
        string relativePath,
        LayerConventionProfiles layerConventions,
        IReadOnlyDictionary<string, int> roleOrder,
        RepoContract? contract)
    {
        string normalized = relativePath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);

        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPriority;
        }

        if (IsInterfaceFile(normalized, fileName))
        {
            return InterfacePriority;
        }

        if (IsEntityPath(normalized, fileName, contract))
        {
            return EntityPriority;
        }

        if (IsAuxiliaryFile(fileName))
        {
            return AuxiliaryPriority;
        }

        if (layerConventions.ResolveByPath(relativePath) is LayerConventionProfile profile
            && roleOrder.TryGetValue(profile.RoleName, out int roleIndex))
        {
            return LayerBasePriority + roleIndex;
        }

        if (IsTestPath(normalized, fileName, contract))
        {
            return TestPriority;
        }

        return DefaultPriority;
    }

    private static IReadOnlyDictionary<string, int> BuildRoleApplyOrder(LayerConventionProfiles conventions)
    {
        var profiles = conventions.GetActiveProfiles().ToList();
        if (profiles.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        foreach (string roleName in KnownRoleApplyOrder)
        {
            if (profiles.Any(profile => profile.RoleName.Equals(roleName, StringComparison.OrdinalIgnoreCase)))
            {
                order[roleName] = index++;
            }
        }

        foreach (LayerConventionProfile profile in profiles
                     .Where(profile => !order.ContainsKey(profile.RoleName))
                     .OrderByDescending(profile => profile.SampleCount)
                     .ThenBy(profile => profile.RoleName, StringComparer.OrdinalIgnoreCase))
        {
            order[profile.RoleName] = index++;
        }

        ApplyConstructorDependencyHints(profiles, order);
        return order;
    }

    /// <summary>
    /// When role B's exemplar constructors commonly take I*RoleA interfaces, apply role A before role B.
    /// </summary>
    private static void ApplyConstructorDependencyHints(
        IReadOnlyList<LayerConventionProfile> profiles,
        Dictionary<string, int> order)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (LayerConventionProfile dependent in profiles)
            {
                if (!order.TryGetValue(dependent.RoleName, out int dependentIndex))
                {
                    continue;
                }

                foreach (string paramType in dependent.RequiredConstructorParamTypes)
                {
                    foreach (LayerConventionProfile dependency in profiles)
                    {
                        if (dependency.RoleName.Equals(dependent.RoleName, StringComparison.OrdinalIgnoreCase)
                            || !TypeNameMatchesDependency(paramType, roleName: dependency.RoleName))
                        {
                            continue;
                        }

                        if (!order.TryGetValue(dependency.RoleName, out int dependencyIndex)
                            || dependencyIndex >= dependentIndex)
                        {
                            continue;
                        }

                        order[dependency.RoleName] = dependentIndex;
                        order[dependent.RoleName] = dependentIndex + 1;
                        ReindexRoles(order);
                        changed = true;
                        break;
                    }

                    if (changed)
                    {
                        break;
                    }
                }

                if (changed)
                {
                    break;
                }
            }
        }
        while (changed);
    }

    private static void ReindexRoles(Dictionary<string, int> order)
    {
        var rolesByRank = order
            .OrderBy(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Key)
            .ToList();

        for (int i = 0; i < rolesByRank.Count; i++)
        {
            order[rolesByRank[i]] = i;
        }
    }

    private static bool TypeNameMatchesDependency(string parameterType, string roleName)
    {
        if (!parameterType.StartsWith('I') || string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        string roleToken = $"I{roleName}";
        return parameterType.StartsWith(roleToken, StringComparison.Ordinal)
               && (parameterType.Length == roleToken.Length || char.IsUpper(parameterType[roleToken.Length]));
    }

    private static bool IsInterfaceFile(string normalizedPath, string fileName)
    {
        if (!fileName.StartsWith('I') || fileName.Length < 3)
        {
            return false;
        }

        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return DotNetRepoContractDiscoverer.IsUnderInterfacesDirectory(normalizedPath)
               || char.IsUpper(fileName[1]);
    }

    private static bool IsEntityPath(string normalizedPath, string fileName, RepoContract? contract)
    {
        if (contract?.Entity is { } entity
            && !string.IsNullOrWhiteSpace(entity.CanonicalDirectory)
            && normalizedPath.StartsWith(entity.CanonicalDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.StartsWith('I');
        }

        return ContainsPathSegment(normalizedPath, "Entities")
               || ContainsPathSegment(normalizedPath, "Models");
    }

    private static bool ContainsPathSegment(string normalizedPath, string segment) =>
        normalizedPath.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.Contains("/" + segment + "/", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.EndsWith("/" + segment, StringComparison.OrdinalIgnoreCase);

    private static bool IsAuxiliaryFile(string fileName) =>
        AuxiliaryFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsTestPath(string normalizedPath, string fileName, RepoContract? contract)
    {
        if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (PathPlacementRule rule in contract?.PathRules ?? Array.Empty<PathPlacementRule>())
        {
            if (!rule.FileSuffix.Equals("Tests.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalizedPath.StartsWith(rule.Directory + "/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Equals(rule.Directory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ContainsPathSegment(normalizedPath, "Tests");
    }
}
