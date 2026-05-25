using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.Compliance.DotNet;

/// <summary>
/// Clones an existing *Tests.cs exemplar for any discovered production layer (repository, service, controller, …).
/// </summary>
internal static class LayerTestTemplateBuilder
{
    private static readonly string[] KnownLayerSuffixes =
    {
        "Repository",
        "Service",
        "Controller",
        "Handler",
        "Provider",
        "Manager",
        "Store"
    };

    public static bool TryBuildFromExemplar(string repoPath, string productionBaseName, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(productionBaseName))
        {
            return false;
        }

        var conventions = TestCoverageAuditor.DiscoverTestConventions(repoPath);
        TestConvention? convention = conventions.FirstOrDefault(c =>
            productionBaseName.EndsWith(
                Path.GetFileNameWithoutExtension(c.ProductionFileSuffix),
                StringComparison.Ordinal))
            ?? conventions.FirstOrDefault();

        if (convention is null)
        {
            return false;
        }

        return TryBuildFromExemplar(repoPath, productionBaseName, convention, out content);
    }

    public static bool TryBuildFromExemplar(
        string repoPath,
        string productionBaseName,
        TestConvention convention,
        out string content)
    {
        content = string.Empty;
        string targetTestFileName = $"{productionBaseName}Tests.cs";
        string exemplarGlob = $"*{Path.GetFileNameWithoutExtension(convention.ProductionFileSuffix)}Tests.cs";

        string testsAbsoluteDir = Path.Combine(
            repoPath,
            convention.TestDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return false;
        }

        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, exemplarGlob, SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                !Path.GetFileName(path).Equals(targetTestFileName, StringComparison.OrdinalIgnoreCase));
        exemplarPath ??= Directory
            .EnumerateFiles(testsAbsoluteDir, "*Tests.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                !Path.GetFileName(path).Equals(targetTestFileName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return false;
        }

        string? exemplarSubject = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(
            Path.GetFileName(exemplarPath));
        if (string.IsNullOrWhiteSpace(exemplarSubject)
            || exemplarSubject.Equals(productionBaseName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        content = ApplySubjectRename(
            File.ReadAllText(exemplarPath),
            exemplarSubject,
            productionBaseName,
            repoPath);
        return CodeExemplarContext.TryValidate(content, out _);
    }

    internal static string ApplySubjectRename(
        string template,
        string exemplarSubject,
        string targetSubject,
        string repoPath)
    {
        foreach (var (from, to) in BuildReplacementPairs(exemplarSubject, targetSubject)
                     .OrderByDescending(pair => pair.From.Length))
        {
            template = ReplaceIdentifier(template, from, to);
        }

        template = TestBootstrapContext.SynchronizeProductionMembers(repoPath, targetSubject, template);
        return TestBootstrapContext.NormalizeResolutionAccess(template, repoPath);
    }

    private static string ReplaceIdentifier(string content, string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || from.Equals(to, StringComparison.Ordinal))
        {
            return content;
        }

        return Regex.Replace(
            content,
            $@"\b{Regex.Escape(from)}\b",
            to,
            RegexOptions.CultureInvariant);
    }

    internal static IReadOnlyList<(string From, string To)> BuildReplacementPairs(
        string exemplarSubject,
        string targetSubject)
    {
        var pairs = new List<(string From, string To)>();
        AddPair(pairs, exemplarSubject, targetSubject);
        AddPair(pairs, $"{exemplarSubject}Tests", $"{targetSubject}Tests");

        if (TrySplitLayer(exemplarSubject, out string exemplarLayer, out string exemplarCore)
            && TrySplitLayer(targetSubject, out string targetLayer, out string targetCore)
            && exemplarLayer.Equals(targetLayer, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(exemplarCore)
            && !string.IsNullOrWhiteSpace(targetCore)
            && !exemplarCore.Equals(targetCore, StringComparison.OrdinalIgnoreCase))
        {
            AddPair(pairs, exemplarCore, targetCore);
            AddPair(pairs, $"I{exemplarCore}{exemplarLayer}", $"I{targetCore}{targetLayer}");
            AddPair(pairs, ToCamel(exemplarCore), ToCamel(targetCore));
            AddPair(pairs, $"{exemplarCore}{exemplarLayer}", $"{targetCore}{targetLayer}");
            AddPair(pairs, $"_{ToCamel(exemplarCore)}{exemplarLayer}", $"_{ToCamel(targetCore)}{targetLayer}");
            AddPair(pairs, $"Persist{exemplarCore}", $"Persist{targetCore}");
            AddPair(pairs, $"ShouldPersist{exemplarCore}", $"ShouldPersist{targetCore}");
            AddPair(pairs, $"Insert_ShouldPersist{exemplarCore}", $"Insert_ShouldPersist{targetCore}");
            AddPair(pairs, $"new {exemplarCore}(", $"new {targetCore}(");
            AddPair(pairs, $"var {ToCamel(exemplarCore)}{exemplarLayer}", $"var {ToCamel(targetCore)}{targetLayer}");
        }

        return pairs;
    }

    private static void AddPair(List<(string From, string To)> pairs, string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from.Equals(to, StringComparison.Ordinal))
        {
            return;
        }

        if (!pairs.Any(pair => pair.From.Equals(from, StringComparison.Ordinal)))
        {
            pairs.Add((from, to));
        }
    }

    private static bool TrySplitLayer(string productionBaseName, out string layerSuffix, out string coreName)
    {
        foreach (string suffix in KnownLayerSuffixes)
        {
            if (productionBaseName.EndsWith(suffix, StringComparison.Ordinal)
                && productionBaseName.Length > suffix.Length)
            {
                layerSuffix = suffix;
                coreName = productionBaseName[..^suffix.Length];
                return true;
            }
        }

        layerSuffix = string.Empty;
        coreName = productionBaseName;
        return false;
    }

    private static string ToCamel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
