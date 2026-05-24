public sealed record AcceptanceCriterion(string Id, string Description);

/// <summary>
/// Structured requirements and definition-of-done criteria for a workflow task.
/// </summary>
public sealed class RequirementsSpec
{
    public string UserStory { get; init; } = string.Empty;
    public IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria { get; init; } = Array.Empty<AcceptanceCriterion>();
    public IReadOnlyList<string> InScope { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutOfScope { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Risks { get; init; } = Array.Empty<string>();

    public bool HasAcceptanceCriteria => AcceptanceCriteria.Count > 0;
}

public sealed record AcceptanceCriterionEvaluation(
    string Id,
    string Description,
    bool Passed,
    string Evidence,
    string Source);

public sealed class AcceptanceCriteriaReport
{
    public IReadOnlyList<AcceptanceCriterionEvaluation> Evaluations { get; init; } = Array.Empty<AcceptanceCriterionEvaluation>();
    public bool AllPassed => Evaluations.Count > 0 && Evaluations.All(evaluation => evaluation.Passed);
    public int PassedCount => Evaluations.Count(evaluation => evaluation.Passed);
    public int FailedCount => Evaluations.Count(evaluation => !evaluation.Passed);
}
