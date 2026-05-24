namespace agents_mcp_rag.Configuration;

public sealed class AppSettings
{
    public string OpenAIKey { get; init; } = string.Empty;
    public string OpenAIModel { get; init; } = "gpt-4o";
    public string OpenAIEmbeddingModel { get; init; } = "text-embedding-3-small";
    public string GitHubPat { get; init; } = string.Empty;
    public string RepoPath { get; init; } = string.Empty;
    public int MaxRecoveryAttempts { get; init; } = 2;
    public int MaxCompilationFixAttempts { get; init; } = 3;
    public CompilationFixContextOptions CompilationFixContext { get; init; } = new();
    public bool UseHybridRag { get; init; } = true;
    public double RagLexicalWeight { get; init; } = 0.55;
    public double RagVectorWeight { get; init; } = 0.45;
    public string DefaultTaskPrompt { get; init; } = "Implement a new feature safely with architecture-first planning and audited delivery.";
    public bool AutoCreatePullRequest { get; init; } = true;
    public string PullRequestBaseBranch { get; init; } = "main";
    public AcceptanceCriteriaOptions AcceptanceCriteria { get; init; } = new();
}
