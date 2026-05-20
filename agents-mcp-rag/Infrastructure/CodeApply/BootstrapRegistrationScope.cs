using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Discovers DI registration scope from bootstrap/composition-root files in the target repo (variable names, block boundaries).
/// </summary>
internal static class BootstrapRegistrationScope
{
    private static readonly Regex CollectionDeclRegex = new(
        @"var\s+(\w+)\s*=\s*new\s+ServiceCollection\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CollectionReturnRegex = new(
        @"return\s+(\w+)\.BuildServiceProvider\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegistrationLineRegex = new(
        @"^\s*(\w+)\.(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    internal static string? BuildContext(string repoPath)
    {
        BootstrapScope? scope = DiscoverPrimary(repoPath);
        if (scope is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("DI registration scope (discovered from this repository — workflow merges lines here automatically):");
        sb.AppendLine($"- Registration file: {scope.RelativePath}");
        sb.AppendLine(
            $"- Collection variable '{scope.CollectionVariable}' is declared only inside the registration block "
            + $"(e.g. var {scope.CollectionVariable} = new ServiceCollection()).");
        sb.AppendLine(
            $"- Append Add* lines after that declaration and before '{scope.ReturnStatementPrefix}'.");
        sb.AppendLine("- Do NOT return bootstrap/composition-root .cs files from agents; do not put Add* lines in Reset/Init or other methods.");
        sb.AppendLine(
            "- Workflow appends only interface+implementation pairs both present in this run's proposed outputs; "
            + "never register types already wired in the discovered registration block.");
        if (scope.SampleRegistrationLines.Count > 0)
        {
            sb.AppendLine("- Mirror sibling registration lines:");
            foreach (string line in scope.SampleRegistrationLines.Take(6))
            {
                sb.AppendLine($"  {line.Trim()}");
            }
        }

        return sb.ToString();
    }

    internal static BootstrapScope? DiscoverFromContent(string content, string relativePath) =>
        TryParse(content, relativePath);

    internal static BootstrapScope? DiscoverPrimary(string repoPath)
    {
        BootstrapScope? best = null;
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

            BootstrapScope? scope = TryParse(File.ReadAllText(path), relative);
            if (scope is null)
            {
                continue;
            }

            int score = scope.SampleRegistrationLines.Count;
            if (relative.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = scope;
            }
        }

        return best;
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

    internal static string BuildRegistrationLine(BootstrapScope? scope, string interfaceName, string? exemplarLine = null)
    {
        string implementationName = interfaceName.StartsWith('I') && interfaceName.Length > 1
            ? interfaceName[1..]
            : interfaceName;
        string variable = scope?.CollectionVariable ?? DiscoverReceiverFromLine(exemplarLine) ?? "services";
        string lifetime = exemplarLine?.Contains("Singleton", StringComparison.OrdinalIgnoreCase) == true
            ? "Singleton"
            : "Scoped";
        return $"{variable}.Add{lifetime}<{interfaceName}, {implementationName}>();";
    }

    internal static string FormatOutOfScopeError(BootstrapScope? scope)
    {
        if (scope is null)
        {
            return "DI registration line is outside the discovered ServiceCollection registration block.";
        }

        return
            $"DI registration must use '{scope.CollectionVariable}' only after its ServiceCollection declaration "
            + $"and before '{scope.ReturnStatementPrefix}' in {scope.RelativePath}.";
    }

    private static BootstrapScope? TryParse(string content, string relativePath)
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

        return new BootstrapScope(relativePath, collectionVariable, returnPrefix, sampleLines);
    }

    private static string? DiscoverReceiverFromLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        Match match = RegistrationLineRegex.Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal sealed record BootstrapScope(
        string RelativePath,
        string CollectionVariable,
        string ReturnStatementPrefix,
        IReadOnlyList<string> SampleRegistrationLines);
}
