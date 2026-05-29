using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Blocks SDK-style duplicate assembly metadata (CS0579) from agent-generated source.
/// </summary>
internal static partial class CSharpAssemblyMetadataGuard
{
    [GeneratedRegex(
        @"\[assembly:\s*(?:System\.Reflection\.)?Assembly(?:Company|Title|Product|Version|FileVersion|Copyright|Description|Configuration|Trademark|Metadata)(?:Attribute)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateProneAssemblyAttributeRegex();

    internal static bool IsAssemblyInfoPath(string relativePath) =>
        relativePath.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);

    internal static bool ContainsDuplicateProneAssemblyMetadata(string content) =>
        !string.IsNullOrWhiteSpace(content)
        && DuplicateProneAssemblyAttributeRegex().IsMatch(content);

    internal static bool TryValidateApply(string relativePath, string content, out string reason)
    {
        reason = string.Empty;
        if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsAssemblyInfoPath(relativePath))
        {
            reason =
                "Rejected AssemblyInfo.cs — SDK projects auto-generate assembly attributes. "
                + "Remove [assembly: Assembly*] metadata from generated output.";
            return false;
        }

        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && ContainsDuplicateProneAssemblyMetadata(content))
        {
            reason =
                "Rejected C# source with [assembly: Assembly*] metadata attributes. "
                + "SDK projects generate these automatically; remove them from hand-written source.";
            return false;
        }

        return true;
    }

    internal static bool ShouldRemoveStrayAssemblyInfoFile(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return false;
        }

        string normalized = absolutePath.Replace('\\', '/');
        if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsAssemblyInfoPath(Path.GetFileName(normalized)))
        {
            return false;
        }

        try
        {
            string content = File.ReadAllText(absolutePath);
            return ContainsDuplicateProneAssemblyMetadata(content)
                   || normalized.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
