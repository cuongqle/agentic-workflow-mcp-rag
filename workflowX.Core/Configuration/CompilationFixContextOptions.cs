namespace workflowX.Configuration;

/// <summary>
/// Controls how much source is inlined into recovery prompts.
/// Mandatory files (build errors, interface definitions, error-directory siblings) are always included.
/// </summary>
public sealed class CompilationFixContextOptions
{
    /// <summary>Max characters for optional (non-mandatory) files. 0 = no limit.</summary>
    public int MaxTotalChars { get; init; } = 200_000;

    /// <summary>Max optional file count after mandatory set. 0 = no limit.</summary>
    public int MaxOptionalFiles { get; init; } = 0;
}
