using workflowX.Infrastructure;

namespace workflowX.Tests.Infrastructure;

public class WorkflowTaskNamingTests
{
    [Fact]
    public void InferTitle_UsesEntityName_WhenPromptMentionsEntity()
    {
        string title = WorkflowTaskNaming.InferTitle("Implement entity called Timesheet with CRUD endpoints");

        Assert.Equal("Implement Timesheet", title);
    }

    [Fact]
    public void InferTitle_UsesFirstLine_ForPlainPrompt()
    {
        string title = WorkflowTaskNaming.InferTitle("Add export to CSV for monthly reports");

        Assert.Equal("Add export to CSV for monthly reports", title);
    }

    [Fact]
    public void InferTitleFromUserStory_ExtractsWantClause()
    {
        string title = WorkflowTaskNaming.InferTitleFromUserStory(
            "As a manager, I want to approve timesheets, so that payroll is accurate.");

        Assert.Equal("Approve timesheets", title);
    }

    [Fact]
    public void ResolveFeatureBranchName_PrefersUserStoryOverTaskTitle()
    {
        var state = new WorkflowState
        {
            Task = new WorkflowTask
            {
                Title = "Legacy title",
                Description = "ignored"
            },
            RequirementsSpec = new RequirementsSpec
            {
                UserStory = "As a user, I want to manage project budgets, so that spending stays on track."
            }
        };

        string branchName = WorkflowTaskNaming.ResolveFeatureBranchName(state);

        Assert.Equal("agents/manage-project-budgets", branchName);
    }

    [Fact]
    public void ResolveFeatureBranchName_UsesSubjectNoun_FromVerbosePrompt()
    {
        var state = new WorkflowState
        {
            Task = new WorkflowTask
            {
                Title = "A timesheet feature to be implemented in the",
                Description = "A timesheet feature to be implemented in the AngularJS WebAPI app with RavenDB."
            }
        };

        string branchName = WorkflowTaskNaming.ResolveFeatureBranchName(state);

        Assert.Equal("agents/timesheet", branchName);
    }

    [Fact]
    public void InferBranchSubject_ExtractsTimesheet_FromFeaturePhrase()
    {
        string? subject = WorkflowTaskNaming.InferBranchSubject(
            "A timesheet feature to be implemented in the AngularJS application");

        Assert.Equal("timesheet", subject);
    }

    [Fact]
    public void ToBranchSlug_StripsStopWords_AndLimitsWords()
    {
        string slug = WorkflowTaskNaming.ToBranchSlug(
            "A timesheet feature to be implemented in the existing codebase");

        Assert.Equal("timesheet", slug);
    }

    [Fact]
    public void RefineTaskFromRequirements_UpdatesTaskTitle()
    {
        var state = new WorkflowState
        {
            Task = new WorkflowTask
            {
                Title = "Add budget feature",
                Description = "Build budget tracking"
            },
            RequirementsSpec = new RequirementsSpec
            {
                UserStory = "As a PM, I want to track project budgets, so that I can control spend."
            }
        };

        WorkflowTaskNaming.RefineTaskFromRequirements(state);

        Assert.Equal("Track project budgets", state.Task.Title);
        Assert.Equal("Build budget tracking", state.Task.Description);
    }
}
