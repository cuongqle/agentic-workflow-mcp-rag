using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Ensures classes implement all members declared on implemented role interfaces (CS0535 prevention).
/// Members satisfied by non-interface base classes (e.g. Repository&lt;T&gt;) are treated as implemented.
/// </summary>
internal static class InterfaceImplementationGuard
{
    private static readonly Regex ClassDeclarationRegex = new(
        @"public\s+class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([^{]+))?",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\binterface\s+(I[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex PublicMethodRegex = new(
        @"public\s+(?:override\s+|virtual\s+|async\s+)*[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    internal static string? BuildRagContext(string repoPath, string? taskSubjectHint)
    {
        InterfaceImplementationPair? exemplar = FindBestImplementationPair(repoPath, taskSubjectHint);
        if (exemplar is null)
        {
            return """
                Interface + implementation pairing:
                - Every method declared on a role interface (I*Repository, I*Service, I*Controller contract) must appear on the implementation class.
                - Members inherited from base classes (e.g. Repository<T>) satisfy IRepository members — add only interface-specific methods on the derived class.
                - Declare the interface first, then implement every member with matching signatures.
                """;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Interface + implementation pairing (mirror exemplar):");
        sb.AppendLine($"- Interface: {exemplar.InterfaceRelativePath}");
        sb.AppendLine($"- Implementation: {exemplar.ImplementationRelativePath}");
        sb.AppendLine("- Implement every method declared on the role interface (names and parameters must match).");
        sb.AppendLine($"- Interface-specific members on exemplar: {string.Join(", ", exemplar.InterfaceOnlyMembers)}");
        sb.AppendLine();
        sb.AppendLine("Exemplar interface:");
        sb.AppendLine(Truncate(exemplar.InterfaceContent, 1000));
        sb.AppendLine();
        sb.AppendLine("Exemplar implementation (interface-specific methods):");
        sb.AppendLine(Truncate(exemplar.ImplementationContent, 1400));
        return sb.ToString();
    }

    internal static bool TryValidate(
        string repoPath,
        string relativePath,
        string content,
        IReadOnlyDictionary<string, HashSet<string>> interfaceDirectMembers,
        out string reason)
    {
        reason = string.Empty;
        if (!ClassDeclarationRegex.IsMatch(content))
        {
            return true;
        }

        Match classMatch = ClassDeclarationRegex.Match(content);
        string className = classMatch.Groups[1].Value;
        var implementedInterfaces = ParseImplementedInterfaces(classMatch.Groups[2].Value);
        if (implementedInterfaces.Count == 0)
        {
            return true;
        }

        var satisfiedMethods = CollectSatisfiedMethods(repoPath, content, classMatch.Groups[2].Value);
        foreach (string iface in implementedInterfaces)
        {
            if (!TryResolveRequiredMembers(iface, interfaceDirectMembers, out HashSet<string>? required)
                || required.Count == 0)
            {
                continue;
            }

            var missing = required.Where(method => !satisfiedMethods.Contains(method)).OrderBy(m => m, StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            reason =
                $"Class {className} must implement all members of {iface}. Missing: {string.Join(", ", missing)}. "
                + $"Declared or base-satisfied members: {string.Join(", ", satisfiedMethods.OrderBy(m => m, StringComparer.Ordinal).Take(16))}.";
            return false;
        }

        return true;
    }

    internal static Dictionary<string, HashSet<string>> BuildDirectMemberCatalog(
        string repoPath,
        IReadOnlyList<GeneratedFile> proposedFiles)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(repoPath, "I*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddDirectInterfaceMembers(File.ReadAllText(file), map);
        }

        foreach (var generated in proposedFiles.Where(f =>
                     Path.GetFileName(f.RelativePath).StartsWith('I')
                     && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            AddDirectInterfaceMembers(generated.Content, map, overwrite: true);
        }

        return map;
    }

    private static HashSet<string> CollectSatisfiedMethods(string repoPath, string content, string inheritanceClause)
    {
        var satisfied = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PublicMethodRegex.Matches(content))
        {
            satisfied.Add(match.Groups[1].Value);
        }

        foreach (string baseType in ParseBaseTypes(inheritanceClause))
        {
            if (baseType.StartsWith('I'))
            {
                continue;
            }

            string? baseContent = ResolveTypeContent(repoPath, baseType);
            if (string.IsNullOrWhiteSpace(baseContent))
            {
                continue;
            }

            foreach (Match match in PublicMethodRegex.Matches(baseContent))
            {
                satisfied.Add(match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(baseContent, @"protected\s+[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline))
            {
                satisfied.Add(match.Groups[1].Value);
            }
        }

        return satisfied;
    }

    private static bool TryResolveRequiredMembers(
        string implementedInterface,
        IReadOnlyDictionary<string, HashSet<string>> interfaceDirectMembers,
        out HashSet<string>? required)
    {
        required = null;
        if (interfaceDirectMembers.TryGetValue(implementedInterface, out required))
        {
            return true;
        }

        string openGeneric = BuildFailureClassifier.StripGenericArity(implementedInterface);
        if (!openGeneric.Equals(implementedInterface, StringComparison.Ordinal)
            && interfaceDirectMembers.TryGetValue(openGeneric, out required))
        {
            return true;
        }

        return false;
    }

    private static List<string> ParseImplementedInterfaces(string inheritanceClause) =>
        inheritanceClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.StartsWith('I') && token.Length > 1)
            .ToList();

    private static List<string> ParseBaseTypes(string inheritanceClause) =>
        inheritanceClause
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTypeName)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToList();

    private static void AddDirectInterfaceMembers(
        string content,
        Dictionary<string, HashSet<string>> map,
        bool overwrite = false)
    {
        foreach (Match ifaceMatch in InterfaceDeclarationRegex.Matches(content))
        {
            string iface = ifaceMatch.Groups[1].Value;
            if (PreExistingContractGuard.IsProtectedInterfaceName(iface))
            {
                continue;
            }

            if (overwrite || !map.ContainsKey(iface))
            {
                map[iface] = new HashSet<string>(StringComparer.Ordinal);
            }

            string body = ExtractInterfaceBody(content, ifaceMatch.Index);
            foreach (Match methodMatch in Regex.Matches(body, @"^\s*(?:[\w<>\[\],\s\?]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{]*\)\s*;", RegexOptions.Multiline))
            {
                map[iface].Add(methodMatch.Groups[1].Value);
            }
        }
    }

    private static string ExtractInterfaceBody(string content, int interfaceIndex)
    {
        int braceStart = content.IndexOf('{', interfaceIndex);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceStart..(i + 1)];
                }
            }
        }

        return content[braceStart..];
    }

    private static string? ResolveTypeContent(string repoPath, string typeName)
    {
        string? byFile = Directory
            .EnumerateFiles(repoPath, $"{typeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byFile))
        {
            return File.ReadAllText(byFile);
        }

        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(path);
            if (Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(typeName)}\b"))
            {
                return content;
            }
        }

        return null;
    }

    private static string NormalizeTypeName(string token)
    {
        string trimmed = token.Trim();
        int generic = trimmed.IndexOf('<');
        return generic > 0 ? trimmed[..generic] : trimmed;
    }

    private static InterfaceImplementationPair? FindBestImplementationPair(string repoPath, string? hint)
    {
        var signals = ExtractNameTokens(hint);
        InterfaceImplementationPair? best = null;
        int bestScore = int.MinValue;

        var profiles = LayerConventionProfileBuilder.Build(repoPath).GetActiveProfiles();
        foreach (var profile in profiles)
        {
            foreach (string implPath in Directory.EnumerateFiles(repoPath, $"*{profile.FileSuffix}", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(implPath).StartsWith('I')
                    || implPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? subject = LayerConventionProfiles.GetSubjectBaseName(Path.GetFileName(implPath), profile);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    continue;
                }

                string interfaceName = LayerConventionProfiles.BuildExpectedInterfaceFileName(subject, profile);
                string? interfacePath = Directory
                    .EnumerateFiles(repoPath, interfaceName, SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(interfacePath))
                {
                    continue;
                }

                string interfaceContent = File.ReadAllText(interfacePath);
                var directMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                AddDirectInterfaceMembers(interfaceContent, directMembers);
                string ifaceKey = Path.GetFileNameWithoutExtension(interfacePath);
                if (!directMembers.TryGetValue(ifaceKey, out HashSet<string>? members)
                    || members.Count == 0)
                {
                    continue;
                }

                int score = signals.Count(s => subject.Contains(s, StringComparison.OrdinalIgnoreCase));
                score += members.Count * 2;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new InterfaceImplementationPair(
                        Path.GetRelativePath(repoPath, interfacePath).Replace('\\', '/'),
                        interfaceContent,
                        Path.GetRelativePath(repoPath, implPath).Replace('\\', '/'),
                        File.ReadAllText(implPath),
                        members.ToList());
                }
            }
        }

        return best;
    }

    private static List<string> ExtractNameTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]{2,}\b")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();
    }

    private static string Truncate(string content, int max) =>
        content.Length > max ? content[..max] + "\n// [truncated]" : content;

    private sealed record InterfaceImplementationPair(
        string InterfaceRelativePath,
        string InterfaceContent,
        string ImplementationRelativePath,
        string ImplementationContent,
        IReadOnlyList<string> InterfaceOnlyMembers);
}
