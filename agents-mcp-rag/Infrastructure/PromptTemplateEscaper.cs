namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Escapes literal <c>{{</c>/<c>}}</c> in repository or LLM-produced text so Semantic Kernel Handlebars
/// does not treat Angular snippets (e.g. <c>{{ timesheet.Date }}</c>) as plugin invocations.
/// </summary>
static class PromptTemplateEscaper
{
    internal static string EscapeLiteralHandlebars(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return text
            .Replace("}}", "{{ \"}}\" }}", StringComparison.Ordinal)
            .Replace("{{", "{{ \"{{\" }}", StringComparison.Ordinal);
    }
}
