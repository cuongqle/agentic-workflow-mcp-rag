namespace agents_mcp_rag.Infrastructure;

internal static class CSharpSyntaxGuard
{
    public static bool TryValidate(string content, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "C# content is empty.";
            return false;
        }

        if (!HasBalancedPairs(content, '(', ')'))
        {
            reason = "Unbalanced parentheses.";
            return false;
        }

        if (!HasBalancedPairs(content, '{', '}'))
        {
            reason = "Unbalanced braces.";
            return false;
        }

        if (!HasBalancedPairs(content, '[', ']'))
        {
            reason = "Unbalanced brackets.";
            return false;
        }

        if (content.Contains(";;", StringComparison.Ordinal))
        {
            reason = "Contains invalid double semicolon (;;).";
            return false;
        }

        foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.EndsWith(",;", StringComparison.Ordinal)
                || trimmed.EndsWith("(;", StringComparison.Ordinal)
                || trimmed.EndsWith("[;", StringComparison.Ordinal))
            {
                reason = $"Suspicious statement terminator in line: {trimmed}";
                return false;
            }
        }

        return true;
    }

    private static bool HasBalancedPairs(string content, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && (inString || inChar))
            {
                escape = true;
                continue;
            }

            if (c == '"' && !inChar)
            {
                inString = !inString;
                continue;
            }

            if (c == '\'' && !inString)
            {
                inChar = !inChar;
                continue;
            }

            if (inString || inChar)
            {
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return depth == 0 && !inString && !inChar;
    }
}
