using System.Text.RegularExpressions;

using workflowX.Infrastructure;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Merges new DI registration lines into an existing test/app composition root without rewriting the file.
/// </summary>
internal static class CompositionRootMerger
{
    private static readonly Regex MalformedUsingRegex = new(
        @"^\s*using\s+[^;{]*\{",
        RegexOptions.Compiled | RegexOptions.Multiline);

    internal static bool TryMergeIntoExisting(
        string existingContent,
        string proposedContent,
        out string mergedContent,
        out string? reason,
        IReadOnlySet<string>? workflowProposedPaths = null,
        string repoPath = "")
    {
        // Preserve all existing registrations on first pass; filtering workflow-new disallowed
        // lines is handled after merge with originalContent as baseline.
        string sanitizedExisting = SanitizeBootstrapContent(existingContent, repoPath);
        mergedContent = sanitizedExisting;
        reason = null;
        BootstrapRegistrationScope.BootstrapScope? scope = BootstrapRegistrationScope.DiscoverFromContent(sanitizedExisting, string.Empty);

        var newLines = ExtractRegistrationLines(proposedContent, scope?.CollectionVariable)
            .Where(line => workflowProposedPaths is null
                           || DependencyWiringAuditor.IsAllowedNewRegistrationLine(line, workflowProposedPaths, repoPath))
            .Where(line => !sanitizedExisting.Contains(line.Trim(), StringComparison.Ordinal))
            .ToList();

        if (newLines.Count == 0)
        {
            if (!PassesBootstrapSyntaxChecks(mergedContent, out reason))
            {
                return false;
            }

            return true;
        }

        if (!TryInsertRegistrationLines(mergedContent, newLines, out mergedContent, out reason))
        {
            return false;
        }

        mergedContent = SanitizeBootstrapContent(mergedContent, repoPath, workflowProposedPaths, existingContent);

        if (!PassesBootstrapSyntaxChecks(mergedContent, out reason))
        {
            return false;
        }

        return CodeExemplarContext.TryValidate(mergedContent, out reason);
    }

    /// <summary>
    /// Post-apply pass: sanitize on-disk composition-root files that were corrupted by earlier apply/merge steps.
    /// </summary>
    internal static IEnumerable<string> RepairCompositionRootFiles(string repoPath)
    {
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

            string original = File.ReadAllText(path);
            string sanitized = SanitizeBootstrapContent(original, repoPath);
            if (!sanitized.Equals(original, StringComparison.Ordinal)
                && PassesBootstrapSyntaxChecks(sanitized, out _))
            {
                File.WriteAllText(path, sanitized);
                yield return $"repaired bootstrap: {relative}";
            }
        }
    }

    internal static string SanitizeBootstrapContent(
        string content,
        string repoPath,
        IReadOnlySet<string>? workflowProposedPaths = null,
        string? originalContent = null) =>
        DependencyWiringAuditor.SanitizeBootstrapRegistrations(
            RemoveOrphanRegistrationLines(content),
            repoPath,
            workflowProposedPaths,
            originalContent);

    internal static bool PassesBootstrapSyntaxChecks(string content, out string? reason)
    {
        reason = null;
        if (MalformedUsingRegex.IsMatch(content))
        {
            reason = "Malformed using directive (namespace brace merged into using line). Each using must end with ';' only.";
            return false;
        }

        if (!content.Contains("namespace ", StringComparison.Ordinal))
        {
            reason = "Bootstrap file must contain a namespace declaration.";
            return false;
        }

        int openBraces = content.Count(c => c == '{');
        int closeBraces = content.Count(c => c == '}');
        if (openBraces != closeBraces)
        {
            reason = $"Unbalanced braces in bootstrap file ({{ count {openBraces}, }} count {closeBraces}).";
            return false;
        }

        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        BootstrapRegistrationScope.BootstrapScope? scope = BootstrapRegistrationScope.DiscoverFromContent(content, string.Empty);

        if (!BootstrapRegistrationScope.TryFindRegistrationBlock(
                lines,
                out int collectionDeclIndex,
                out int returnIndex,
                out string collectionVariable))
        {
            // Program/minimal-host composition roots may legitimately use existing Add* lines
            // without a local ServiceCollection declaration + BuildServiceProvider return block.
            return true;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (!BootstrapRegistrationScope.IsRegistrationLine(lines[i], collectionVariable))
            {
                continue;
            }

            if (i <= collectionDeclIndex || i >= returnIndex)
            {
                reason = BootstrapRegistrationScope.FormatOutOfScopeError(scope);
                return false;
            }
        }

        return true;
    }

    private static readonly Regex RegistrationLineRegex = new(
        @"^\s*(\w+)\.(?:Add(?:Scoped|Singleton|Transient)|RegisterType)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static string RemoveOrphanRegistrationLines(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        if (!BootstrapRegistrationScope.TryFindRegistrationBlock(
                lines,
                out int collectionDeclIndex,
                out int returnIndex,
                out string collectionVariable))
        {
            return content;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (!BootstrapRegistrationScope.IsRegistrationLine(lines[i], collectionVariable))
            {
                continue;
            }

            if (i <= collectionDeclIndex || i >= returnIndex)
            {
                lines[i] = string.Empty;
            }
        }

        return string.Join(Environment.NewLine, lines.Where(line => line != string.Empty));
    }

    private static List<string> ExtractRegistrationLines(string content, string? knownCollectionVariable)
    {
        var lines = new List<string>();
        foreach (string line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!RegistrationLineRegex.IsMatch(line))
            {
                continue;
            }

            string variable = RegistrationLineRegex.Match(line).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(knownCollectionVariable)
                && !variable.Equals(knownCollectionVariable, StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(line.TrimEnd());
        }

        return lines;
    }

    private static bool TryInsertRegistrationLines(
        string existingContent,
        IReadOnlyList<string> newLines,
        out string mergedContent,
        out string? reason)
    {
        reason = null;
        var lines = existingContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        if (!BootstrapRegistrationScope.TryFindRegistrationBlock(
                lines,
                out int collectionDeclIndex,
                out int insertIndex,
                out string collectionVariable))
        {
            // Fallback for Program.cs/minimal-host style files where registrations exist
            // but there is no local ServiceCollection declaration/return block.
            if (TryInsertNearExistingRegistrations(lines, newLines, out mergedContent))
            {
                reason = null;
                return true;
            }

            reason = "Could not find DI registration block (ServiceCollection declaration ... return ...BuildServiceProvider()).";
            mergedContent = existingContent;
            return false;
        }

        string indent = InferIndent(lines, insertIndex, collectionDeclIndex, collectionVariable);
        foreach (string registration in newLines)
        {
            string normalized = registration.Trim();
            if (!normalized.StartsWith($"{collectionVariable}.", StringComparison.Ordinal))
            {
                Match receiver = RegistrationLineRegex.Match(normalized);
                if (receiver.Success)
                {
                    normalized = normalized.Replace(
                        $"{receiver.Groups[1].Value}.",
                        $"{collectionVariable}.",
                        StringComparison.Ordinal);
                }
            }

            if (!normalized.StartsWith(indent, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(indent))
            {
                normalized = indent + normalized.TrimStart();
            }

            lines.Insert(insertIndex++, normalized);
        }

        mergedContent = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static bool TryInsertNearExistingRegistrations(
        List<string> lines,
        IReadOnlyList<string> newLines,
        out string mergedContent)
    {
        mergedContent = string.Join(Environment.NewLine, lines);
        if (newLines.Count == 0)
        {
            return true;
        }

        string? preferredReceiver = null;
        foreach (string line in newLines)
        {
            Match m = RegistrationLineRegex.Match(line);
            if (m.Success)
            {
                preferredReceiver = m.Groups[1].Value;
                break;
            }
        }

        int insertIndex = -1;
        string indent = string.Empty;
        string? actualReceiver = preferredReceiver;

        if (!string.IsNullOrWhiteSpace(preferredReceiver))
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!RegistrationLineRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                Match m = RegistrationLineRegex.Match(lines[i]);
                if (!m.Success || !m.Groups[1].Value.Equals(preferredReceiver, StringComparison.Ordinal))
                {
                    continue;
                }

                insertIndex = i + 1;
                indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                break;
            }
        }

        if (insertIndex < 0)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (!RegistrationLineRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                Match m = RegistrationLineRegex.Match(lines[i]);
                if (!m.Success)
                {
                    continue;
                }

                actualReceiver = m.Groups[1].Value;
                insertIndex = i + 1;
                indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                break;
            }
        }

        if (insertIndex < 0)
        {
            return false;
        }

        foreach (string registration in newLines)
        {
            string normalized = registration.Trim();
            if (!string.IsNullOrWhiteSpace(actualReceiver))
            {
                Match receiver = RegistrationLineRegex.Match(normalized);
                if (receiver.Success && !receiver.Groups[1].Value.Equals(actualReceiver, StringComparison.Ordinal))
                {
                    normalized = normalized.Replace(
                        $"{receiver.Groups[1].Value}.",
                        $"{actualReceiver}.",
                        StringComparison.Ordinal);
                }
            }

            if (!string.IsNullOrWhiteSpace(indent) && !normalized.StartsWith(indent, StringComparison.Ordinal))
            {
                normalized = indent + normalized.TrimStart();
            }

            lines.Insert(insertIndex++, normalized);
        }

        mergedContent = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static string InferIndent(
        List<string> lines,
        int nearIndex,
        int collectionDeclIndex,
        string collectionVariable)
    {
        for (int i = nearIndex - 1; i > collectionDeclIndex; i--)
        {
            if (BootstrapRegistrationScope.IsRegistrationLine(lines[i], collectionVariable))
            {
                string line = lines[i];
                int nonSpace = line.Length - line.TrimStart().Length;
                return line[..nonSpace];
            }
        }

        if (collectionDeclIndex >= 0 && collectionDeclIndex < lines.Count)
        {
            string declLine = lines[collectionDeclIndex];
            int nonSpace = declLine.Length - declLine.TrimStart().Length;
            return new string(' ', nonSpace + 4);
        }

        return "            ";
    }
}
