using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

internal static class CSharpApplySupport
{
    internal static bool TryValidate(string content, out string reason)
    {
        reason = string.Empty;
        string trimmed = content.Trim();
        bool hasCSharpShape =
            (trimmed.Contains("namespace ", StringComparison.Ordinal)
             || trimmed.Contains("class ", StringComparison.Ordinal)
             || trimmed.Contains("interface ", StringComparison.Ordinal))
            && trimmed.Contains("{", StringComparison.Ordinal)
            && trimmed.Contains("}", StringComparison.Ordinal);
        if (!hasCSharpShape)
        {
            reason = "C# output missing expected type/namespace and block structure.";
            return false;
        }

        if (!CodeExemplarContext.TryValidate(trimmed, out string syntaxReason))
        {
            reason = $"C# syntax check failed: {syntaxReason}";
            return false;
        }

        return true;
    }

    internal static bool TryValidateDotNet(string relativePath, string content, out string? reason)
    {
        reason = null;

        if (!CSharpAssemblyMetadataGuard.TryValidateApply(relativePath, content, out string assemblyReason))
        {
            reason = assemblyReason;
            return false;
        }

        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !TryValidate(content, out reason))
        {
            return false;
        }

        return true;
    }

    internal static bool IsDotNetSourcePath(string relativePath)
    {
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension is ".cs" or ".csproj";
    }

    internal static List<GeneratedFile> OrderForApply(
        IReadOnlyList<GeneratedFile> files,
        LayerConventionProfiles layerConventions,
        RepoContract? contract = null) =>
        CSharpApplyOrderSupport.OrderForApply(files, layerConventions, contract);
}
