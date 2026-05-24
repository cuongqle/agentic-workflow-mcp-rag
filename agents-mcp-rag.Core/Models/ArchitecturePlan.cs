public sealed record ArchitectureDeliverable(string Path, string Description = "");

/// <summary>
/// Parsed architecture plan — from structured JSON or markdown fallback.
/// </summary>
public sealed class ArchitecturePlan
{
    public string Rationale { get; init; } = string.Empty;
    public IReadOnlyList<ArchitectureDeliverable> BackendFiles { get; init; } = Array.Empty<ArchitectureDeliverable>();
    public IReadOnlyList<ArchitectureDeliverable> FrontendFiles { get; init; } = Array.Empty<ArchitectureDeliverable>();
    public string TestStrategy { get; init; } = string.Empty;
    public string RollbackNotes { get; init; } = string.Empty;

    public bool HasBackendDeliverables => BackendFiles.Count > 0;
    public bool HasFrontendDeliverables => FrontendFiles.Count > 0;

    public IReadOnlyList<string> BackendPaths =>
        BackendFiles.Select(file => file.Path).ToList();

    public IReadOnlyList<string> FrontendPaths =>
        FrontendFiles.Select(file => file.Path).ToList();
}
