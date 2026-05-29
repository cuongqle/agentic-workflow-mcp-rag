using System.Text;

namespace workflowX.Infrastructure.Compliance.DotNet;

/// <summary>
/// Static prompt guidance for test bootstrap and DI setup (no runtime discovery).
/// </summary>
internal static class TestBootstrapContext
{
    internal static string BuildContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Test bootstrap resolution guidance (prompt-first, no discovery):");
        sb.AppendLine("- Identify bootstrap/composition-root type and setup flow by reading sibling *Tests.cs files first.");
        sb.AppendLine("- Use only public bootstrap APIs already used in existing tests.");
        sb.AppendLine("- Never access private bootstrap members or ServiceProvider directly.");
        sb.AppendLine("- If bootstrap type or resolve method is ambiguous, stop and report low confidence instead of guessing.");
        sb.AppendLine("DI registration guidance (prompt-first, no discovery):");
        sb.AppendLine("- Edit only existing composition-root/bootstrap registration files when DI wiring changes are required.");
        sb.AppendLine("- Add registrations inside the existing ServiceCollection/container registration block only.");
        sb.AppendLine("- Append new interface-to-implementation registrations; do not remove or rewrite existing lines.");
        sb.AppendLine("- Do not add DI registration lines in tests/controllers/reset/init helpers or unrelated methods.");
        sb.AppendLine("- Mirror lifetime from nearby registrations for similar roles (Scoped/Singleton/Transient).");
        sb.AppendLine("- If no clear registration block exists, stop and report low confidence instead of guessing.");

        sb.AppendLine("- Mirror existing [TestInitialize]/Setup shape from sibling *Tests.cs files (reset/init first, then dependency resolution).");
        sb.AppendLine("- Resolve dependencies through that bootstrap's public methods only (never via private ServiceProvider).");

        sb.AppendLine("Test data/type guidance (prompt-first, no catalog discovery):");
        sb.AppendLine("- Never assign quoted strings to non-string model/entity temporal properties.");
        sb.AppendLine("- Use typed temporal values (DateTime/DateTimeOffset/DateOnly/TimeOnly) matching on-disk model/entity property types.");
        sb.AppendLine("- Use Parse/TryParse only when converting from string input; do not parse values already typed.");
        sb.AppendLine("- If property type is uncertain, inspect the model/entity file first and mirror existing tests; do not guess.");

        sb.AppendLine();
        sb.AppendLine("Test project reference guidance (PackageReference + ProjectReference):");
        foreach (string line in TestProjectPackagePromptSupport.BuildRuleLines())
        {
            sb.AppendLine($"- {line}");
        }

        return sb.ToString();
    }
}
