using System.Text;
using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Ensures mutation actions validate foreign keys the same way existing controllers do:
/// resolve related records through injected role repositories before persisting.
/// </summary>
internal static class ControllerMutationValidationGuard
{
    private static readonly Regex ForeignKeyPropertyRegex = new(
        @"public\s+int\s+([A-Za-z][A-Za-z0-9_]*)Id\s*\{",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MutationMethodRegex = new(
        @"\[(?:HttpPost|HttpPut|HttpPatch)[^\]]*\][\s\S]*?(?:public|protected|internal)\s+(?:async\s+)?(?:[\w<>\[\],\s\?\.]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\)\s*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Role repository lookup that passes an entity/route *Id foreign-key property as an argument.
    /// Method name is discovered from exemplars (GetById, Load, Find, etc.).
    /// </summary>
    private static readonly Regex RepositoryLookupOnForeignKeyRegex = new(
        @"(?<repo>[A-Za-z_][A-Za-z0-9_]*Repository)\.(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\.\s*(?<fk>[A-Za-z][A-Za-z0-9_]*)Id\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string? BuildRagContext(string repoPath)
    {
        if (!TryDiscoverExemplarMutationValidation(
                repoPath,
                out string exemplarPath,
                out IReadOnlyList<string> foreignKeys,
                out IReadOnlyList<string> lookupMethods))
        {
            return """
                Mutation validation (mirror existing controllers):
                - Before Create/Update/Post actions, resolve each *Id foreign key through the related injected role repository using the same lookup pattern as sibling controllers in this repo.
                - Return NotFound (or equivalent) when a related record does not exist.
                """;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Mutation validation (mirror exemplar controller in this repo):");
        sb.AppendLine($"- Exemplar: {exemplarPath}");
        sb.AppendLine("- Before persisting, resolve each *Id foreign key through the matching role repository (use declared repository members — not a fixed method name).");
        if (lookupMethods.Count > 0)
        {
            sb.AppendLine($"- Exemplar lookup member(s): {string.Join(", ", lookupMethods)}");
        }

        sb.AppendLine($"- Exemplar validates: {string.Join(", ", foreignKeys.Select(fk => $"{fk}Id"))}");
        return sb.ToString();
    }

    internal static bool TryValidate(
        string repoPath,
        string controllerRelativePath,
        string controllerContent,
        string? entityContent,
        out string reason)
    {
        reason = string.Empty;
        if (!TryDiscoverExemplarMutationValidation(repoPath, out _, out _, out _))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(entityContent))
        {
            return true;
        }

        IReadOnlyList<string> foreignKeys = ExtractForeignKeyProperties(entityContent);
        if (foreignKeys.Count == 0)
        {
            return true;
        }

        IReadOnlyList<string> mutationBodies = ExtractMutationMethodBodies(controllerContent);
        if (mutationBodies.Count == 0)
        {
            return true;
        }

        var missingChecks = new List<string>();
        foreach (string body in mutationBodies)
        {
            foreach (string foreignKeyStem in foreignKeys)
            {
                string propertyName = $"{foreignKeyStem}Id";
                if (MethodBodyValidatesForeignKey(body, propertyName))
                {
                    continue;
                }

                missingChecks.Add(propertyName);
            }
        }

        if (missingChecks.Count == 0)
        {
            return true;
        }

        string distinct = string.Join(", ", missingChecks.Distinct(StringComparer.Ordinal));
        reason =
            $"Mutation actions must resolve foreign key(s) {distinct} through the related role repository before persisting, "
            + "matching existing controller mutation validation in this repository.";
        return false;
    }

    internal static IReadOnlyList<string> ExtractForeignKeyProperties(string entityContent)
    {
        var keys = new List<string>();
        foreach (Match match in ForeignKeyPropertyRegex.Matches(entityContent))
        {
            string stem = match.Groups[1].Value;
            if (!stem.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(stem);
            }
        }

        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool HasValidationEvidence(string repoPath, IReadOnlyList<GeneratedFile> proposedFiles)
    {
        foreach (GeneratedFile file in proposedFiles.Where(f =>
                     f.RelativePath.Contains("Controller", StringComparison.OrdinalIgnoreCase)
                     && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            string? entityContent = ResolveEntityContent(repoPath, proposedFiles, file.RelativePath);
            if (TryValidate(repoPath, file.RelativePath, file.Content, entityContent, out _))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryDiscoverExemplarMutationValidation(
        string repoPath,
        out string exemplarRelativePath,
        out IReadOnlyList<string> foreignKeys,
        out IReadOnlyList<string> lookupMethods)
    {
        exemplarRelativePath = string.Empty;
        foreignKeys = Array.Empty<string>();
        lookupMethods = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return false;
        }

        foreach (string path in Directory.EnumerateFiles(repoPath, "*Controller.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            string content = File.ReadAllText(path);
            string? entityContent = ResolveEntityContent(repoPath, Array.Empty<GeneratedFile>(), relative);
            if (entityContent is null)
            {
                continue;
            }

            IReadOnlyList<string> entityForeignKeys = ExtractForeignKeyProperties(entityContent);
            if (entityForeignKeys.Count == 0)
            {
                continue;
            }

            foreach (string body in ExtractMutationMethodBodies(content))
            {
                if (!entityForeignKeys.Any(fk => MethodBodyValidatesForeignKey(body, $"{fk}Id")))
                {
                    continue;
                }

                exemplarRelativePath = relative;
                foreignKeys = entityForeignKeys;
                lookupMethods = ExtractLookupMethods(body, entityForeignKeys);
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractLookupMethods(string methodBody, IReadOnlyList<string> foreignKeyStems)
    {
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in RepositoryLookupOnForeignKeyRegex.Matches(methodBody))
        {
            string fk = match.Groups["fk"].Value;
            if (foreignKeyStems.Any(stem => stem.Equals(fk, StringComparison.OrdinalIgnoreCase)))
            {
                methods.Add(match.Groups["method"].Value);
            }
        }

        return methods.OrderBy(method => method, StringComparer.Ordinal).ToList();
    }

    private static bool MethodBodyValidatesForeignKey(string methodBody, string foreignKeyPropertyName)
    {
        foreach (Match match in RepositoryLookupOnForeignKeyRegex.Matches(methodBody))
        {
            if ($"{match.Groups["fk"].Value}Id".Equals(foreignKeyPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractMutationMethodBodies(string controllerContent)
    {
        var bodies = new List<string>();
        foreach (Match match in MutationMethodRegex.Matches(controllerContent))
        {
            int openBraceIndex = match.Index + match.Length - 1;
            if (openBraceIndex < controllerContent.Length
                && controllerContent[openBraceIndex] == '{'
                && TryReadBracedBlock(controllerContent, openBraceIndex, out string body))
            {
                bodies.Add(body);
            }
        }

        return bodies;
    }

    internal static string? ResolveEntityContentForCompliance(
        string repoPath,
        IReadOnlyList<GeneratedFile> proposedFiles,
        string controllerRelativePath) =>
        ResolveEntityContent(repoPath, proposedFiles, controllerRelativePath);

    private static string? ResolveEntityContent(
        string repoPath,
        IReadOnlyList<GeneratedFile> proposedFiles,
        string controllerRelativePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(controllerRelativePath);
        if (!fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string entityName = fileName[..^"Controller".Length];
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return null;
        }

        GeneratedFile? proposedEntity = proposedFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f.RelativePath).Equals(entityName, StringComparison.OrdinalIgnoreCase)
            && (f.RelativePath.Contains("/Entities/", StringComparison.OrdinalIgnoreCase)
                || f.RelativePath.Contains("\\Entities\\", StringComparison.OrdinalIgnoreCase)));
        if (proposedEntity is not null)
        {
            return proposedEntity.Content;
        }

        string? diskPath = Directory
            .EnumerateFiles(repoPath, $"{entityName}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                (path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase)
                 || path.Contains("\\Entities\\", StringComparison.OrdinalIgnoreCase))
                && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
        return diskPath is null ? null : File.ReadAllText(diskPath);
    }

    private static bool TryReadBracedBlock(string content, int openBraceIndex, out string block)
    {
        block = string.Empty;
        int depth = 0;
        for (int i = openBraceIndex; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    block = content[(openBraceIndex + 1)..i];
                    return true;
                }
            }
        }

        return false;
    }
}
