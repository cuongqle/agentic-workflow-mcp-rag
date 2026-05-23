namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Stack-agnostic generated content checks (empty, prose, minimum size).
/// </summary>
internal static class ApplyContentGuard
{
    internal static bool TryValidateCommonShape(string content, bool isOverwritingExistingFile, out string reason)
    {
        reason = string.Empty;
        string trimmed = content.Trim();
        int lineCount = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            reason = "Generated content is empty.";
            return false;
        }

        if (trimmed.Length < 40)
        {
            reason = "Generated content is too short to be a real source file.";
            return false;
        }

        if (LooksLikePlainEnglishSentence(trimmed))
        {
            reason = "Generated content looks like prose, not source code.";
            return false;
        }

        if (trimmed.Contains("Corrected the", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("for frontend interaction", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Generated content appears to be explanatory text, not code.";
            return false;
        }

        if (isOverwritingExistingFile && lineCount < 4)
        {
            reason = "Refused to overwrite existing file with very short output.";
            return false;
        }

        return true;
    }

    private static bool LooksLikePlainEnglishSentence(string content) =>
        !content.Contains("{", StringComparison.Ordinal)
        && !content.Contains("}", StringComparison.Ordinal)
        && !content.Contains(";", StringComparison.Ordinal)
        && content.EndsWith(".", StringComparison.Ordinal)
        && content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length <= 2;
}
