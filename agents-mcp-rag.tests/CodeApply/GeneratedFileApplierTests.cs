using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.CodeApply;

public class GeneratedFileApplierTests
{
    [Fact]
    public async Task ApplyAsync_rejects_prose_only_content()
    {
        using var repo = new TempRepo();
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(false, true));
        state.Stage = WorkflowStage.Implementing;
        state.Frontend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "web/modules/employee/controllers/note.js",
                    Content = "This file explains the approach but contains no code."
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.AppliedFiles);
        Assert.Single(result.RejectedFiles);
    }

    [Fact]
    public async Task ApplyAsync_writes_valid_javascript_file()
    {
        using var repo = new TempRepo();
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(false, true));
        state.Stage = WorkflowStage.Implementing;
        state.Frontend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "web/modules/employee/controllers/widget.js",
                    Content = """
                        angular.module('app').controller('WidgetCtrl', function () {
                            const value = 1;
                        });
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Single(result.AppliedFiles);
        Assert.Empty(result.RejectedFiles);
        Assert.True(File.Exists(Path.Combine(repo.Path, "web/modules/employee/controllers/widget.js")));
    }

    [Fact]
    public async Task ApplyAsync_rejects_path_outside_repo_root()
    {
        using var repo = new TempRepo();
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(false, true));
        state.Stage = WorkflowStage.Implementing;
        state.Frontend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "../../../etc/passwd.js",
                    Content = "const token = 'x';"
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.AppliedFiles);
        Assert.NotEmpty(result.RejectedFiles);
    }

    [Fact]
    public async Task RollbackAsync_restores_previous_content()
    {
        using var repo = new TempRepo();
        string relative = "web/readme.txt";
        string originalPath = repo.WriteFile(relative, "original");
        string fullPath = Path.GetFullPath(originalPath);

        var changes = new List<AppliedFileChange>
        {
            new(relative, ExistedBeforeApply: true, PreviousContent: "original")
        };

        File.WriteAllText(fullPath, "modified");
        await GeneratedFileApplier.RollbackAsync(repo.Path, changes);

        Assert.Equal("original", File.ReadAllText(fullPath));
    }
}
