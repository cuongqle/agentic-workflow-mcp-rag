using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Validates instance member access (this.X, field.X) against declared and inherited members from base types in the repo.
/// </summary>
internal static class ClassMemberAccessGuard
{
    private static readonly Regex ClassDeclarationRegex = new(
        @"public\s+class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([^{]+))?",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"public\s+interface\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([^{]+))?",
        RegexOptions.Compiled);

    private static readonly Regex InstanceMemberDeclarationRegex = new(
        @"(?:public|protected|private|internal)\s+(?:static\s+|readonly\s+|virtual\s+|override\s+)*([\w<>\[\],\s\?]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ThisMemberCallRegex = new(
        @"\b(this|@this)\.([A-Za-z_][A-Za-z0-9_]*)\s*\.",
        RegexOptions.Compiled);

    internal static IEnumerable<string> CollectInheritedTypeNames(string repoPath, string typeName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(typeName);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            string? content = ResolveTypeContent(repoPath, current);
            if (string.IsNullOrWhiteSpace(content)
                || !TryParseTypeDeclaration(content, out _, out IReadOnlyList<string> baseTypes))
            {
                continue;
            }

            foreach (string baseType in baseTypes)
            {
                string normalized = NormalizeTypeName(baseType);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    queue.Enqueue(normalized);
                }
            }
        }
    }

    internal static bool ShouldValidateFile(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (relativePath.Contains("/UnitTest/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("\\UnitTest\\", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("RepositoryTest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (DependencyWiringAuditor.IsCompositionRootPath(relativePath))
        {
            return false;
        }

        return true;
    }

    internal static bool TryValidate(string repoPath, string relativePath, string content, out string reason)
    {
        reason = string.Empty;
        if (!ShouldValidateFile(relativePath))
        {
            return true;
        }

        if (!TryParseClassDeclaration(content, out string className, out IReadOnlyList<string> baseTypes))
        {
            return true;
        }

        var accessible = CollectAccessibleMembers(repoPath, className, content, baseTypes);

        foreach (Match match in ThisMemberCallRegex.Matches(content))
        {
            string member = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(member))
            {
                continue;
            }

            if (!accessible.Contains(member))
            {
                reason =
                    $"Instance member '{member}' is not declared on {className} or its base types. "
                    + $"Accessible members: {string.Join(", ", accessible.OrderBy(m => m, StringComparer.Ordinal))}.";
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> CollectAccessibleMembers(
        string repoPath,
        string className,
        string classContent,
        IReadOnlyList<string> baseTypes)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        AddMembersFromContent(classContent, members, includePrivate: true);

        var visited = new HashSet<string>(StringComparer.Ordinal) { className };
        foreach (string baseType in baseTypes)
        {
            CollectBaseMembers(repoPath, NormalizeTypeName(baseType), visited, members);
        }

        return members;
    }

    private static void CollectBaseMembers(
        string repoPath,
        string baseTypeName,
        HashSet<string> visitedTypes,
        HashSet<string> members)
    {
        if (string.IsNullOrWhiteSpace(baseTypeName) || !visitedTypes.Add(baseTypeName))
        {
            return;
        }

        string? baseContent = ResolveTypeContent(repoPath, baseTypeName);
        if (string.IsNullOrWhiteSpace(baseContent))
        {
            return;
        }

        AddMembersFromContent(baseContent, members, includePrivate: false);

        if (TryParseTypeDeclaration(baseContent, out _, out IReadOnlyList<string> parentBases))
        {
            foreach (string parent in parentBases)
            {
                CollectBaseMembers(repoPath, NormalizeTypeName(parent), visitedTypes, members);
            }
        }
    }

    private static void AddMembersFromContent(string content, HashSet<string> members, bool includePrivate)
    {
        foreach (Match match in InstanceMemberDeclarationRegex.Matches(content))
        {
            string name = match.Groups[2].Value;
            if (!includePrivate && IsPrivateMemberDeclaration(content, match.Index))
            {
                continue;
            }

            members.Add(name);
            RegisterMemberAlias(members, name);
        }

        foreach (Match match in Regex.Matches(content, @"(?:public|protected)\s+[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline))
        {
            members.Add(match.Groups[1].Value);
        }
    }

    private static bool IsPrivateMemberDeclaration(string content, int memberIndex)
    {
        int lineStart = content.LastIndexOf('\n', Math.Max(0, memberIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = content.IndexOf('\n', memberIndex);
        if (lineEnd < 0)
        {
            lineEnd = content.Length;
        }

        string line = content[lineStart..lineEnd].TrimStart();
        return line.StartsWith("private ", StringComparison.Ordinal);
    }

    private static void RegisterMemberAlias(HashSet<string> members, string name)
    {
        if (name.StartsWith('_') && name.Length > 2)
        {
            members.Add(char.ToUpperInvariant(name[1]) + name[2..]);
        }
    }

    private static string? ResolveTypeContent(string repoPath, string typeName)
    {
        string? byFileName = Directory
            .EnumerateFiles(repoPath, $"{typeName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byFileName))
        {
            return File.ReadAllText(byFileName);
        }

        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileContent = File.ReadAllText(path);
            if (Regex.IsMatch(fileContent, $@"\b(class|interface)\s+{Regex.Escape(typeName)}\b"))
            {
                return fileContent;
            }
        }

        return null;
    }

    private static bool TryParseClassDeclaration(
        string content,
        out string className,
        out IReadOnlyList<string> baseTypes) =>
        TryParseTypeDeclaration(content, out className, out baseTypes);

    private static bool TryParseTypeDeclaration(
        string content,
        out string typeName,
        out IReadOnlyList<string> baseTypes)
    {
        typeName = string.Empty;
        baseTypes = Array.Empty<string>();
        Match match = InterfaceDeclarationRegex.Match(content);
        if (!match.Success)
        {
            match = ClassDeclarationRegex.Match(content);
            if (!match.Success)
            {
                return false;
            }
        }

        typeName = match.Groups[1].Value;
        if (!match.Groups[2].Success || string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            return true;
        }

        baseTypes = match.Groups[2].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTypeName)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToList();

        return true;
    }

    private static string NormalizeTypeName(string token)
    {
        string trimmed = token.Trim();
        int genericStart = trimmed.IndexOf('<');
        if (genericStart > 0)
        {
            trimmed = trimmed[..genericStart];
        }

        return trimmed;
    }
}
