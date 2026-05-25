namespace workflowX.Orchestration;

public static class ProposedFileSupport
{
    public static List<GeneratedFile> GetAllProposedFiles(WorkflowState state) =>
        (state.Backend?.ProposedFiles ?? [])
            .Concat(state.Frontend?.ProposedFiles ?? [])
            .Concat(state.Recovery?.ProposedFiles ?? [])
            .ToList();

    /// <summary>
    /// Proposed files with applied paths replaced by current on-disk content (for compliance/audit refresh).
    /// </summary>
    public static List<GeneratedFile> GetFilesForComplianceValidation(WorkflowState state)
    {
        var byPath = new Dictionary<string, GeneratedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (GeneratedFile file in GetAllProposedFiles(state))
        {
            byPath[NormalizeRelativePath(file.RelativePath)] = file;
        }

        foreach (string appliedPath in state.AppliedFiles)
        {
            string normalized = NormalizeRelativePath(appliedPath);
            string absolute = Path.Combine(state.RepoPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            byPath[normalized] = new GeneratedFile
            {
                RelativePath = normalized,
                Content = File.ReadAllText(absolute)
            };
        }

        return byPath.Values.ToList();
    }

    private static string NormalizeRelativePath(string path) =>
        path.Trim().Replace('\\', '/');
}
