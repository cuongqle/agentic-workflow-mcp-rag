using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Filename parsing shared by placement validation and companion rules (no hard-coded artifact names).
/// </summary>
internal static class DeliverableFileNameSupport
{
    private static readonly Regex RoleSuffixFromFileRegex = new(
        @"^(?<subject>[A-Za-z][A-Za-z0-9_]*)(?<role>[A-Z][a-zA-Z0-9]{2,})$",
        RegexOptions.Compiled);

    internal static bool TryGetRoleSuffix(string fileNameWithoutExtension, out string roleSuffix)
    {
        roleSuffix = string.Empty;
        if (!TryGetSubjectAndRole(fileNameWithoutExtension, out _, out roleSuffix))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(roleSuffix);
    }

    internal static bool TryGetSubjectAndRole(
        string fileNameWithoutExtension,
        out string subject,
        out string roleSuffix)
    {
        subject = string.Empty;
        roleSuffix = string.Empty;
        Match match = RoleSuffixFromFileRegex.Match(fileNameWithoutExtension);
        if (!match.Success)
        {
            return false;
        }

        subject = match.Groups["subject"].Value;
        roleSuffix = match.Groups["role"].Value;
        return !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(roleSuffix);
    }
}
