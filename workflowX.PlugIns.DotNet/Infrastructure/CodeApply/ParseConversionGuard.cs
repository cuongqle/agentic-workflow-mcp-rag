using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Guards against invalid primitive Parse(...) usage on already-typed values.
/// Example: int.Parse(timesheet.EmployeeId) when EmployeeId is already int.
/// </summary>
internal static class ParseConversionGuard
{
    private static readonly Regex TypedMemberRegex = new(
        @"\b(?<type>int|long|short|byte|double|float|decimal|bool|Guid|DateTime|DateOnly|TimeOnly|string)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex ParseInvocationRegex = new(
        @"\b(?<type>int|long|short|byte|double|float|decimal|bool|Guid|DateTime|DateOnly|TimeOnly)\.Parse\s*\(\s*(?<arg>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)",
        RegexOptions.Compiled);

    internal static bool TryValidate(string content, out string reason)
    {
        reason = string.Empty;
        Dictionary<string, string> typedSymbols = CollectTypedSymbols(content);

        foreach (Match match in ParseInvocationRegex.Matches(content))
        {
            string parseType = match.Groups["type"].Value;
            string argExpression = match.Groups["arg"].Value;
            string symbol = argExpression.Contains('.', StringComparison.Ordinal)
                ? argExpression.Split('.').Last()
                : argExpression;

            if (!typedSymbols.TryGetValue(symbol, out string symbolType))
            {
                continue;
            }

            if (!parseType.Equals(symbolType, StringComparison.Ordinal))
            {
                continue;
            }

            reason =
                $"Invalid conversion: {match.Value} parses '{argExpression}' even though '{symbol}' is already typed as {symbolType}. "
                + "Use the value directly and only Parse(...) when the source is string.";
            return false;
        }

        return true;
    }

    private static Dictionary<string, string> CollectTypedSymbols(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in TypedMemberRegex.Matches(content))
        {
            map[match.Groups["name"].Value] = match.Groups["type"].Value;
        }

        return map;
    }
}
