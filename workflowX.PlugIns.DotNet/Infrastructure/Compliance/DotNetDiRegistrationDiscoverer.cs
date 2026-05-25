using System.Text;
using System.Text.RegularExpressions;

using workflowX.Infrastructure;

namespace workflowX.Infrastructure.Compliance.DotNet;

internal static class DotNetDiRegistrationDiscoverer
{
    internal static readonly Regex CollectionDeclRegex = new(
        @"var\s+(\w+)\s*=\s*new\s+ServiceCollection\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static readonly Regex CollectionReturnRegex = new(
        @"return\s+(\w+)\.BuildServiceProvider\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static readonly Regex RegistrationLineRegex = new(
        @"^\s*(\w+)\.(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    internal static RegistrationScopeConvention? TryParseFromContent(string content, string relativePath)
    {
        Match decl = CollectionDeclRegex.Match(content);
        if (!decl.Success)
        {
            return null;
        }

        string collectionVariable = decl.Groups[1].Value;
        Match ret = CollectionReturnRegex.Match(content);
        string returnPrefix = ret.Success
            ? $"return {ret.Groups[1].Value}.BuildServiceProvider()"
            : $"return {collectionVariable}.BuildServiceProvider()";

        var sampleLines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => IsRegistrationLine(line, collectionVariable))
            .Select(line => line.TrimEnd())
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList();

        return new RegistrationScopeConvention(
            RegistrationFramework.DotNetDependencyInjection,
            relativePath,
            collectionVariable,
            $"var {collectionVariable} = new ServiceCollection()",
            returnPrefix,
            sampleLines);
    }

    internal static RegistrationScopeConvention DiscoverPrimary(string repoPath)
    {
        RegistrationScopeConvention? best = null;
        int bestScore = int.MinValue;

        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            if (!DependencyWiringAuditor.IsCompositionRootPath(relative))
            {
                continue;
            }

            RegistrationScopeConvention? convention = TryParseFromContent(File.ReadAllText(path), relative);
            if (convention is null)
            {
                continue;
            }

            int score = convention.SampleRegistrationLines.Count;
            if (relative.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = convention;
            }
        }

        return best ?? RegistrationScopeConvention.None;
    }

    internal static bool TryFindRegistrationBlock(
        IReadOnlyList<string> lines,
        out int collectionDeclIndex,
        out int insertBeforeIndex,
        out string collectionVariable)
    {
        collectionDeclIndex = -1;
        insertBeforeIndex = -1;
        collectionVariable = string.Empty;

        for (int i = 0; i < lines.Count; i++)
        {
            Match decl = CollectionDeclRegex.Match(lines[i]);
            if (!decl.Success)
            {
                continue;
            }

            collectionDeclIndex = i;
            collectionVariable = decl.Groups[1].Value;

            for (int j = i + 1; j < lines.Count; j++)
            {
                Match ret = CollectionReturnRegex.Match(lines[j]);
                if (ret.Success && ret.Groups[1].Value.Equals(collectionVariable, StringComparison.Ordinal))
                {
                    insertBeforeIndex = j;
                    return true;
                }
            }

            for (int j = lines.Count - 1; j > i; j--)
            {
                if (IsRegistrationLine(lines[j], collectionVariable))
                {
                    insertBeforeIndex = j + 1;
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    internal static bool IsRegistrationLine(string line, string collectionVariable) =>
        RegistrationLineRegex.IsMatch(line)
        && RegistrationLineRegex.Match(line).Groups[1].Value.Equals(collectionVariable, StringComparison.Ordinal);

    internal static string? DiscoverReceiverFromLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        Match match = RegistrationLineRegex.Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static string FormatRagContext(RegistrationScopeConvention convention)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DI registration scope (discovered from this repository — workflow merges lines here automatically):");
        sb.AppendLine($"- Registration file: {convention.HubRelativePath}");
        sb.AppendLine(
            $"- Collection variable '{convention.ReceiverExpression}' is declared only inside the registration block "
            + $"(e.g. {convention.BlockStartExample ?? $"var {convention.ReceiverExpression} = new ServiceCollection()"}).");
        sb.AppendLine(
            $"- Append Add* lines after that declaration and before '{convention.BlockEndMarker}'.");
        sb.AppendLine("- Do NOT return bootstrap/composition-root .cs files from agents; do not put Add* lines in Reset/Init or other methods.");
        sb.AppendLine(
            "- Workflow appends only interface+implementation pairs both present in this run's proposed outputs; "
            + "never remove or replace lines already in the discovered registration block.");
        if (convention.SampleRegistrationLines.Count > 0)
        {
            sb.AppendLine("- Mirror sibling registration lines:");
            foreach (string line in convention.SampleRegistrationLines.Take(6))
            {
                sb.AppendLine($"  {line.Trim()}");
            }
        }

        return sb.ToString();
    }

    internal static string BuildRegistrationLine(
        RegistrationScopeConvention convention,
        string interfaceName,
        string? exemplarLine = null)
    {
        string implementationName = interfaceName.StartsWith('I') && interfaceName.Length > 1
            ? interfaceName[1..]
            : interfaceName;
        string variable = !string.IsNullOrWhiteSpace(convention.ReceiverExpression)
            ? convention.ReceiverExpression
            : DiscoverReceiverFromLine(exemplarLine) ?? "services";
        string lifetime = exemplarLine?.Contains("Singleton", StringComparison.OrdinalIgnoreCase) == true
            ? "Singleton"
            : "Scoped";
        return $"{variable}.Add{lifetime}<{interfaceName}, {implementationName}>();";
    }

    internal static string FormatOutOfScopeError(RegistrationScopeConvention convention) =>
        $"DI registration must use '{convention.ReceiverExpression}' only after its ServiceCollection declaration "
        + $"and before '{convention.BlockEndMarker}' in {convention.HubRelativePath}.";
}
