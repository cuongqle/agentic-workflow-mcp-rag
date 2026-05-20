using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Validates instance member access (this.X, field.X) against declared and inherited members from base types in the repo.
/// </summary>
internal static class ClassMemberAccessGuard
{
    private static readonly Regex ClassDeclarationRegex = new(
        @"public\s+class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([^{]+))?",
        RegexOptions.Compiled);

    private static readonly Regex InstanceMemberDeclarationRegex = new(
        @"(?:public|protected|private|internal)\s+(?:static\s+|readonly\s+|virtual\s+|override\s+)*([\w<>\[\],\s\?]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ThisMemberCallRegex = new(
        @"\b(this|@this)\.([A-Za-z_][A-Za-z0-9_]*)\s*\.",
        RegexOptions.Compiled);

    internal static string? BuildBaseTypeContext(string repoPath, string classContent, string? classFileName = null)
    {
        if (!TryParseClassDeclaration(classContent, out string className, out IReadOnlyList<string> baseTypes))
        {
            return null;
        }

        var members = CollectAccessibleMembers(repoPath, className, classContent, baseTypes);
        if (members.Count == 0)
        {
            return null;
        }

        return $"Accessible instance members for {className} (from type and bases): {string.Join(", ", members.OrderBy(m => m, StringComparer.Ordinal))}";
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
        AddMembersFromContent(classContent, members, includePrivate: false);

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

        if (!TryParseClassDeclaration(baseContent, out _, out IReadOnlyList<string> parentBases))
        {
            return;
        }

        foreach (string parent in parentBases)
        {
            CollectBaseMembers(repoPath, NormalizeTypeName(parent), visitedTypes, members);
        }
    }

    private static void AddMembersFromContent(string content, HashSet<string> members, bool includePrivate)
    {
        foreach (Match match in InstanceMemberDeclarationRegex.Matches(content))
        {
            string name = match.Groups[2].Value;
            if (!includePrivate)
            {
                int index = match.Index;
                string prefix = content[..index];
                int lastModifier = Math.Max(
                    prefix.LastIndexOf("private", StringComparison.Ordinal),
                    Math.Max(prefix.LastIndexOf("protected", StringComparison.Ordinal),
                        Math.Max(prefix.LastIndexOf("public", StringComparison.Ordinal),
                            prefix.LastIndexOf("internal", StringComparison.Ordinal))));
                if (lastModifier >= 0)
                {
                    string modifier = content[lastModifier..].TrimStart().Split(' ')[0];
                    if (modifier.Equals("private", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
            }

            members.Add(name);
        }

        foreach (Match match in Regex.Matches(content, @"(?:public|protected)\s+[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline))
        {
            members.Add(match.Groups[1].Value);
        }
    }

    private static HashSet<string> ExtractDeclaredFieldNames(string content)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     content,
                     @"(?:private|protected|public)\s+(?:readonly\s+)?[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*;",
                     RegexOptions.Multiline))
        {
            fields.Add(match.Groups[1].Value);
        }

        return fields;
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
            if (Regex.IsMatch(fileContent, $@"\bclass\s+{Regex.Escape(typeName)}\b"))
            {
                return fileContent;
            }
        }

        return null;
    }

    private static bool TryParseClassDeclaration(
        string content,
        out string className,
        out IReadOnlyList<string> baseTypes)
    {
        className = string.Empty;
        baseTypes = Array.Empty<string>();
        Match match = ClassDeclarationRegex.Match(content);
        if (!match.Success)
        {
            return false;
        }

        className = match.Groups[1].Value;
        if (!match.Groups[2].Success || string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            return true;
        }

        baseTypes = match.Groups[2].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTypeName)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Where(type => !type.StartsWith("I", StringComparison.Ordinal))
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
