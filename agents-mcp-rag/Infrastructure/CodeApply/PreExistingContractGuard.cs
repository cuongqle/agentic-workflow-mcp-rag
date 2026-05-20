using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Prevents the workflow from rewriting pre-existing interfaces, base stores, and infrastructure types.
/// </summary>
internal static class PreExistingContractGuard
{
    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\binterface\s+(I[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceMethodRegex = new(
        @"^\s*(?:[\w<>\[\],\s\?]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{]*\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> ProtectedInterfaceNames = new(StringComparer.Ordinal)
    {
        "IDbStore",
        "IRepository",
        "IEntity"
    };

    private static readonly string[] ForbiddenInfrastructureApiTokens =
    {
        "SaveChanges",
        "SaveChangesAsync",
        "DbContext",
        ".Update(",
        "DatabaseFacade"
    };

    internal static bool TryValidateOverwrite(
        string relativePath,
        string? existingContent,
        string proposedContent,
        IReadOnlySet<string> workflowProposedPaths,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(existingContent))
        {
            return TryValidateNewSource(proposedContent, out reason);
        }

        if (IsProtectedInfrastructurePath(relativePath))
        {
            reason =
                $"Refused to modify pre-existing infrastructure file '{relativePath}'. Adapt new code to existing contracts; do not change store/base/interface definitions.";
            return false;
        }

        foreach (Match match in InterfaceDeclarationRegex.Matches(existingContent))
        {
            string iface = match.Groups[1].Value;
            if (IsWorkflowIntroducedRepositoryInterface(iface, relativePath, workflowProposedPaths))
            {
                continue;
            }

            if (!InterfaceMembersEqual(existingContent, proposedContent, iface))
            {
                reason =
                    $"Refused to change existing interface '{iface}' in '{relativePath}'. Use only members already declared in the repository (do not add SaveChanges, Update, or other invented APIs).";
                return false;
            }
        }

        return TryValidateNewSource(proposedContent, out reason);
    }

    internal static bool IsProtectedInterfaceName(string interfaceName) =>
        ProtectedInterfaceNames.Contains(interfaceName);

    internal static bool TryValidateNewSource(string content, out string reason)
    {
        reason = string.Empty;
        foreach (string token in ForbiddenInfrastructureApiTokens)
        {
            if (content.Contains(token, StringComparison.Ordinal))
            {
                reason =
                    $"Generated code references forbidden infrastructure API '{token}'. This repository uses existing IDbStore patterns (Save/Load/Query) — do not invent EF-style members.";
                return false;
            }
        }

        foreach (Match match in InterfaceDeclarationRegex.Matches(content))
        {
            string iface = match.Groups[1].Value;
            if (!IsProtectedInterfaceName(iface))
            {
                continue;
            }

            reason =
                $"Refused to generate or redefine protected interface '{iface}'. Only add new I*Repository interfaces for the new feature.";
            return false;
        }

        return true;
    }

    private static bool IsProtectedInfrastructurePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);

        if (fileName.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("IDbStore.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("IRepository.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("IEntity.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("RavenDbStore", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("InMemoryDbStore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Contains("/Db/DbStore/", StringComparison.OrdinalIgnoreCase)
               && fileName.StartsWith("I", StringComparison.Ordinal)
               && !fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkflowIntroducedRepositoryInterface(
        string interfaceName,
        string relativePath,
        IReadOnlySet<string> workflowProposedPaths)
    {
        if (!interfaceName.EndsWith("Repository", StringComparison.Ordinal))
        {
            return false;
        }

        return workflowProposedPaths.Contains(relativePath.Replace('\\', '/'));
    }

    private static bool InterfaceMembersEqual(string existingContent, string proposedContent, string interfaceName)
    {
        var existing = ExtractInterfaceMethods(existingContent, interfaceName);
        var proposed = ExtractInterfaceMethods(proposedContent, interfaceName);
        if (proposed.Count == 0 && existing.Count > 0)
        {
            return false;
        }

        return existing.SetEquals(proposed);
    }

    private static HashSet<string> ExtractInterfaceMethods(string content, string interfaceName)
    {
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match decl in InterfaceDeclarationRegex.Matches(content))
        {
            if (!decl.Groups[1].Value.Equals(interfaceName, StringComparison.Ordinal))
            {
                continue;
            }

            string body = ExtractInterfaceBody(content, decl.Index);
            foreach (Match method in InterfaceMethodRegex.Matches(body))
            {
                methods.Add(method.Groups[1].Value);
            }
        }

        return methods;
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

        return string.Empty;
    }
}
