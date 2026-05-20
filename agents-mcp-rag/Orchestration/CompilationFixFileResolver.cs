using System.Text.RegularExpressions;

static class CompilationFixFileResolver
{
    public static List<string> DetermineAllowedFiles(WorkflowState state)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in state.BuildValidation?.Findings ?? Enumerable.Empty<AgentFinding>())
        {
            foreach (var file in ExtractFilePathsFromBuildMessage(finding.Message, state.RepoPath))
            {
                files.Add(file);
            }
        }
        foreach (var issue in state.ComplianceIssues)
        {
            foreach (var file in ExtractFilePathsFromBuildMessage(issue, state.RepoPath))
            {
                files.Add(file);
            }
        }

        var declarationIndex = BuildTypeDeclarationIndex(state.RepoPath);
        ExpandWithBuildSymbolHints(state, files, declarationIndex);

        if (files.Count == 0)
        {
            foreach (var path in state.Backend?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
            foreach (var path in state.Frontend?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
            foreach (var path in state.Recovery?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
        }

        ExpandWithContractDependencies(state.RepoPath, files, declarationIndex);
        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(80).ToList();
    }

    private static IEnumerable<string> ExtractFilePathsFromBuildMessage(string message, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Enumerable.Empty<string>();
        }

        var matches = Regex.Matches(message, @"([A-Za-z0-9_\-./\\]+\.cs)(?:\(\d+,\d+\))?");
        return matches
            .Select(match => NormalizeToRepoRelativePath(match.Groups[1].Value, repoPath))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToRepoRelativePath(string path, string repoPath)
    {
        string normalized = path.Replace('\\', '/');
        if (!Path.IsPathRooted(normalized))
        {
            return normalized.TrimStart('/');
        }

        string repoRoot = Path.GetFullPath(repoPath).Replace('\\', '/').TrimEnd('/');
        string absolute = Path.GetFullPath(path).Replace('\\', '/');
        if (absolute.StartsWith(repoRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return absolute[(repoRoot.Length + 1)..];
        }

        return normalized;
    }

    private static void ExpandWithContractDependencies(
        string repoPath,
        HashSet<string> files,
        Dictionary<string, List<string>>? declarationIndex = null)
    {
        declarationIndex ??= BuildTypeDeclarationIndex(repoPath);
        var queue = new Queue<(string RelativePath, int Depth)>(files.Select(path => (path, 0)));
        var visited = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        const int maxDepth = 2;
        const int maxTotalFiles = 120;

        while (queue.Count > 0 && files.Count < maxTotalFiles)
        {
            var (relativePath, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute) || !absolute.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            var referencedTypes = ExtractReferencedTypeNames(content);
            foreach (var typeName in referencedTypes)
            {
                if (!declarationIndex.TryGetValue(typeName, out var declaringFiles))
                {
                    continue;
                }

                foreach (var declaring in declaringFiles)
                {
                    if (visited.Contains(declaring))
                    {
                        continue;
                    }

                    visited.Add(declaring);
                    files.Add(declaring);
                    queue.Enqueue((declaring, depth + 1));
                    if (files.Count >= maxTotalFiles)
                    {
                        break;
                    }
                }

                if (files.Count >= maxTotalFiles)
                {
                    break;
                }
            }

            // Pull nearby contracts/helpers in the same directory (generic, bounded).
            string? directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                foreach (var sibling in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                             .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                         && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                             .Take(12))
                {
                    string relativeSibling = Path.GetRelativePath(repoPath, sibling).Replace('\\', '/');
                    if (visited.Contains(relativeSibling))
                    {
                        continue;
                    }
                    visited.Add(relativeSibling);
                    files.Add(relativeSibling);
                    queue.Enqueue((relativeSibling, depth + 1));
                    if (files.Count >= maxTotalFiles)
                    {
                        break;
                    }
                }
            }
        }
    }

    private static Dictionary<string, List<string>> BuildTypeDeclarationIndex(string repoPath)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            foreach (Match match in Regex.Matches(content, @"\b(class|interface|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)"))
            {
                string typeName = match.Groups[2].Value;
                if (!index.TryGetValue(typeName, out var list))
                {
                    list = new List<string>();
                    index[typeName] = list;
                }
                if (!list.Any(existing => existing.Equals(relative, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(relative);
                }
            }
        }

        return index;
    }

    private static HashSet<string> ExtractReferencedTypeNames(string content)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]*)\b"))
        {
            string token = match.Groups[1].Value;
            if (token is "Namespace" or "Class" or "Interface" or "Public" or "Private" or "Protected" or "Internal" or "Static")
            {
                continue;
            }
            referenced.Add(token);
        }

        foreach (Match match in Regex.Matches(content, @"\bI([A-Z][A-Za-z0-9_]*)\b"))
        {
            referenced.Add("I" + match.Groups[1].Value);
        }

        return referenced;
    }

    private static void ExpandWithBuildSymbolHints(
        WorkflowState state,
        HashSet<string> files,
        Dictionary<string, List<string>> declarationIndex)
    {
        foreach (var finding in state.BuildValidation?.Findings ?? Enumerable.Empty<AgentFinding>())
        {
            foreach (var symbol in ExtractMissingSymbolsFromBuildMessage(finding.Message))
            {
                if (!declarationIndex.TryGetValue(symbol, out var candidates))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    files.Add(candidate);
                }
            }
        }
    }

    private static IEnumerable<string> ExtractMissingSymbolsFromBuildMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Enumerable.Empty<string>();
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(message, @"name '([A-Za-z_][A-Za-z0-9_]*)' could not be found", RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }
        foreach (Match match in Regex.Matches(message, @"'([A-Za-z_][A-Za-z0-9_]*)' does not contain a definition for", RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        return symbols;
    }
}
