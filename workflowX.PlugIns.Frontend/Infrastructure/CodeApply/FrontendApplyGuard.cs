namespace workflowX.Infrastructure.CodeApply.Frontend;

/// <summary>
/// Frontend (JS/TS/HTML) apply validation — reject invalid generated script/markup.
/// </summary>
internal static class FrontendApplyGuard
{
    internal static bool TryValidateByExtension(string relativePath, string content, out string? reason)
    {
        reason = null;
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (extension is ".js" or ".ts" or ".tsx" or ".jsx")
        {
            string trimmed = content.Trim();
            bool hasScriptShape =
                trimmed.Contains("function", StringComparison.Ordinal)
                || trimmed.Contains("=>", StringComparison.Ordinal)
                || trimmed.Contains("class ", StringComparison.Ordinal)
                || trimmed.Contains("const ", StringComparison.Ordinal)
                || trimmed.Contains("let ", StringComparison.Ordinal)
                || trimmed.Contains("var ", StringComparison.Ordinal)
                || trimmed.Contains("angular.", StringComparison.OrdinalIgnoreCase);
            if (!hasScriptShape)
            {
                reason = "Script output missing expected function/class/module constructs.";
                return false;
            }
        }
        else if (extension == ".html")
        {
            string trimmed = content.Trim();
            if (!trimmed.Contains("<", StringComparison.Ordinal) || !trimmed.Contains(">", StringComparison.Ordinal))
            {
                reason = "HTML output missing markup tags.";
                return false;
            }
        }

        return true;
    }

    internal static bool IsFrontendSourcePath(string relativePath)
    {
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension is ".js" or ".ts" or ".tsx" or ".jsx" or ".html";
    }
}
