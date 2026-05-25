using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Rejects stub/placeholder implementations before they are written to disk.
/// </summary>
internal static class PlaceholderImplementationGuard
{
    private static readonly Regex PlaceholderCommentRegex = new(
        @"//\s*(TODO|FIXME|HACK|Implement\b|Methods?\s+for\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool ContainsPlaceholderMarkers(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Contains("throw new NotImplementedException", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (PlaceholderCommentRegex.IsMatch(content))
        {
            return true;
        }

        if (content.Contains("can be added here", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    internal static bool TryValidate(string content, out string reason)
    {
        if (!ContainsPlaceholderMarkers(content))
        {
            reason = string.Empty;
            return true;
        }

        reason = "File contains placeholder/stub markers (TODO, Implement here, NotImplementedException, etc.). "
                 + "Return a complete implementation mirroring the layer exemplar in RAG.";
        return false;
    }
}
