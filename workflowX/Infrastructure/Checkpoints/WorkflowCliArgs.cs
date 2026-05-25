using workflowX.Configuration;

namespace workflowX.Infrastructure;

internal static class WorkflowCliArgs
{
    internal sealed record ParsedArgs(string TaskPrompt, WorkflowResumeOptions ResumeOptions);

    public static ParsedArgs Parse(string[] args, string defaultTaskPrompt, WorkflowResumeOptions settingsDefaults)
    {
        bool resumeFromCheckpoint = settingsDefaults.ResumeFromCheckpoint;
        WorkflowStage? startFromStage = settingsDefaults.StartFromStage;
        string? checkpointPath = settingsDefaults.CheckpointPath;
        var taskTokens = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token.Equals("--no-resume", StringComparison.OrdinalIgnoreCase))
            {
                resumeFromCheckpoint = false;
                continue;
            }

            if (token.Equals("--from", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !Enum.TryParse(args[++i], ignoreCase: true, out WorkflowStage parsedStage))
                {
                    throw new ArgumentException("Missing or invalid stage after --from. Example: --from Implementing");
                }

                startFromStage = parsedStage;
                resumeFromCheckpoint = true;
                continue;
            }

            if (token.Equals("--checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++i]))
                {
                    throw new ArgumentException("Missing path after --checkpoint.");
                }

                checkpointPath = args[i];
                resumeFromCheckpoint = true;
                continue;
            }

            taskTokens.Add(token);
        }

        string taskPrompt = taskTokens.Count > 0
            ? string.Join(' ', taskTokens)
            : defaultTaskPrompt;

        return new ParsedArgs(
            taskPrompt,
            new WorkflowResumeOptions
            {
                ResumeFromCheckpoint = resumeFromCheckpoint,
                StartFromStage = startFromStage,
                CheckpointPath = checkpointPath
            });
    }
}
