using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

sealed class WorkflowOrchestrator
{
    private readonly ArchitectureAgent _architectureAgent;
    private readonly ObserverAgent _observerAgent;
    private readonly BackendDeveloperAgent _backendDeveloperAgent;
    private readonly FrontendDeveloperAgent _frontendDeveloperAgent;
    private readonly BuildValidationAgent _buildValidationAgent;
    private readonly AuditorAgent _auditorAgent;
    private readonly RecoveryAgent _recoveryAgent;
    private readonly GitHubMcpAdapter _mcpAdapter;
    private readonly int _maxRecoveryAttempts;
    private readonly int _maxCompilationFixAttempts;

    public WorkflowOrchestrator(
        ArchitectureAgent architectureAgent,
        ObserverAgent observerAgent,
        BackendDeveloperAgent backendDeveloperAgent,
        FrontendDeveloperAgent frontendDeveloperAgent,
        BuildValidationAgent buildValidationAgent,
        AuditorAgent auditorAgent,
        RecoveryAgent recoveryAgent,
        GitHubMcpAdapter mcpAdapter,
        int maxRecoveryAttempts,
        int maxCompilationFixAttempts)
    {
        _architectureAgent = architectureAgent;
        _observerAgent = observerAgent;
        _backendDeveloperAgent = backendDeveloperAgent;
        _frontendDeveloperAgent = frontendDeveloperAgent;
        _buildValidationAgent = buildValidationAgent;
        _auditorAgent = auditorAgent;
        _recoveryAgent = recoveryAgent;
        _mcpAdapter = mcpAdapter;
        _maxRecoveryAttempts = Math.Max(1, maxRecoveryAttempts);
        _maxCompilationFixAttempts = Math.Max(1, maxCompilationFixAttempts);
    }

    public async Task<WorkflowState> RunAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        state.AddTimeline("Workflow queued.");
        await _mcpAdapter.PublishStatusAsync($"Queued: {state.Task.Title}");

        state.Stage = WorkflowStage.Planning;
        state.AddTimeline("Architecture planning started.");
        state.Architecture = await _architectureAgent.ExecuteAsync(state, cancellationToken);
        await _mcpAdapter.PublishStatusAsync("Architecture plan completed.");

        state.Stage = WorkflowStage.Implementing;
        state.AddTimeline("Implementation started.");
        var llmOutputQualityFindings = new List<AgentFinding>();
        var rollbackChanges = new Dictionary<string, AppliedFileChange>(StringComparer.OrdinalIgnoreCase);

        var backendTask = _backendDeveloperAgent.ExecuteAsync(state, cancellationToken);
        var frontendTask = _frontendDeveloperAgent.ExecuteAsync(state, cancellationToken);
        await Task.WhenAll(backendTask, frontendTask);
        state.Backend = backendTask.Result;
        state.Frontend = frontendTask.Result;
        await _mcpAdapter.PublishStatusAsync("Backend and frontend implementation plans completed.");

        var applyResult = await GeneratedFileApplier.ApplyAsync(state);
        if (applyResult.AppliedFiles.Count > 0)
        {
            state.AddTimeline($"Generated files applied: {string.Join(", ", applyResult.AppliedFiles)}");
            await _mcpAdapter.PublishStatusAsync($"Applied {applyResult.AppliedFiles.Count} generated files to repository.");
            CaptureRollbackChanges(rollbackChanges, applyResult.AppliedChanges);
        }
        else
        {
            state.AddTimeline("No generated source files were produced by developer agents.");
        }
        if (applyResult.RejectedFiles.Count > 0)
        {
            foreach (var rejected in applyResult.RejectedFiles)
            {
                var finding = new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"Rejected generated file '{rejected.RelativePath}': {rejected.Reason}"
                };
                llmOutputQualityFindings.Add(finding);
                state.AddTimeline($"Generation quality finding: [High] {finding.Message}");
                state.ComplianceIssues.Add(finding.Message);
            }
            await _mcpAdapter.PublishStatusAsync($"Rejected {applyResult.RejectedFiles.Count} low-quality generated file(s).");
        }

        var complianceFindings = CollectComplianceFindings(state);
        complianceFindings.AddRange(llmOutputQualityFindings);
        foreach (var finding in complianceFindings)
        {
            state.AddTimeline($"Compliance finding: [{finding.Severity}] {finding.Message}");
            state.ComplianceIssues.Add($"[{finding.Severity}] {finding.Message}");
        }
        if (complianceFindings.Count > 0)
        {
            await _mcpAdapter.PublishStatusAsync($"Detected {complianceFindings.Count} backend contract gaps.");
        }
        if (HasBlockingFindings(complianceFindings))
        {
            state.BuildValidation = new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = "Compilation fix triggered by blocking repository contract findings.",
                Findings = complianceFindings
                    .Where(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker)
                    .ToList()
            };
            await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);

            complianceFindings = CollectComplianceFindings(state);
            complianceFindings.AddRange(llmOutputQualityFindings);
        }

        state.Stage = WorkflowStage.Integrating;
        state.AddTimeline("Build validation started.");
        state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
        if (state.BuildValidation.Findings.Count > 0)
        {
            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding: [{finding.Severity}] {finding.Message}");
            }
            await _mcpAdapter.PublishStatusAsync($"Build validation found {state.BuildValidation.Findings.Count} issue(s).");

            await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);
        }
        else
        {
            state.AddTimeline("Build validation passed.");
            await _mcpAdapter.PublishStatusAsync("Build validation passed.");
        }

        state.AddTimeline("Integration observation started.");
        state.Observer = await _observerAgent.ExecuteAsync(state, cancellationToken);
        await _mcpAdapter.PublishStatusAsync("Observer review completed.");

        state.Stage = WorkflowStage.Auditing;
        state.AddTimeline("Audit started.");
        state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
        state.Audit.Findings.AddRange(complianceFindings);
        if (state.BuildValidation is not null)
        {
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }
        await _mcpAdapter.PublishStatusAsync("Audit completed.");

        while (AuditorAgent.HasBlockingFindings(state.Audit) && state.RecoveryAttemptCount < _maxRecoveryAttempts)
        {
            state.Stage = WorkflowStage.Recovering;
            state.RecoveryAttemptCount++;
            state.AddTimeline($"Recovery attempt {state.RecoveryAttemptCount} started.");
            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            await _mcpAdapter.PublishStatusAsync($"Recovery attempt {state.RecoveryAttemptCount} completed.");

            var recoveredResult = await GeneratedFileApplier.ApplyAsync(state);
            if (recoveredResult.AppliedFiles.Count > 0)
            {
                state.AddTimeline($"Recovery files applied: {string.Join(", ", recoveredResult.AppliedFiles)}");
                await _mcpAdapter.PublishStatusAsync($"Applied {recoveredResult.AppliedFiles.Count} recovery file(s).");
                CaptureRollbackChanges(rollbackChanges, recoveredResult.AppliedChanges);
            }
            else
            {
                state.AddTimeline("Recovery produced no concrete file changes.");
            }
            if (recoveredResult.RejectedFiles.Count > 0)
            {
                foreach (var rejected in recoveredResult.RejectedFiles)
                {
                    var finding = new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"Rejected recovery file '{rejected.RelativePath}': {rejected.Reason}"
                    };
                    llmOutputQualityFindings.Add(finding);
                    state.AddTimeline($"Recovery quality finding: [High] {finding.Message}");
                    state.ComplianceIssues.Add(finding.Message);
                }
                await _mcpAdapter.PublishStatusAsync($"Rejected {recoveredResult.RejectedFiles.Count} low-quality recovery file(s).");
            }

            state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
            if (state.BuildValidation.Findings.Count > 0)
            {
                foreach (var finding in state.BuildValidation.Findings)
                {
                    state.AddTimeline($"Build finding after recovery: [{finding.Severity}] {finding.Message}");
                }
                await _mcpAdapter.PublishStatusAsync($"Build still failing after recovery attempt {state.RecoveryAttemptCount}.");
                await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);
            }
            else
            {
                state.AddTimeline($"Build passed after recovery attempt {state.RecoveryAttemptCount}.");
                await _mcpAdapter.PublishStatusAsync($"Build passed after recovery attempt {state.RecoveryAttemptCount}.");
            }

            state.Stage = WorkflowStage.Auditing;
            state.AddTimeline($"Re-audit after recovery attempt {state.RecoveryAttemptCount}.");
            state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
            var postRecoveryCompliance = CollectComplianceFindings(state);
            postRecoveryCompliance.AddRange(llmOutputQualityFindings);
            foreach (var finding in postRecoveryCompliance)
            {
                state.ComplianceIssues.Add($"[{finding.Severity}] {finding.Message}");
            }
            state.Audit.Findings.AddRange(postRecoveryCompliance);
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }

        ApplyTestFailureReleasePolicy(state);

        if (ShouldAttemptTestQuarantine(state))
        {
            await TryQuarantineTestArtifactsAsync(state, rollbackChanges, cancellationToken);
            RefreshAutomatedComplianceAuditFindings(state, llmOutputQualityFindings);
        }

        if (AuditorAgent.HasBlockingFindings(state.Audit))
        {
            bool productionBuildPassed = state.BuildValidation?.ProductionBuildPassed == true;
            if (!productionBuildPassed && state.BuildValidation is not null && state.BuildValidation.Findings.Count > 0 && rollbackChanges.Count > 0)
            {
                await GeneratedFileApplier.RollbackAsync(state.RepoPath, rollbackChanges.Values.ToList());
                state.AddTimeline("Rolled back generated changes because production compilation remained failing.");
                await _mcpAdapter.PublishStatusAsync("Rolled back generated changes due to unresolved production build failures.");
            }
            else if (productionBuildPassed)
            {
                state.AddTimeline("Production build passed; unresolved test-project issues were quarantined or downgraded.");
                await _mcpAdapter.PublishStatusAsync("Proceeding with production changes despite unresolved test-project issues.");
            }

            if (AuditorAgent.HasBlockingFindings(state.Audit))
            {
                state.Stage = WorkflowStage.Blocked;
                state.AddTimeline("Workflow blocked by unresolved audit findings.");
                await _mcpAdapter.PublishStatusAsync("Workflow blocked.");
                return state;
            }
        }

        state.Stage = WorkflowStage.ReadyForPR;
        state.AddTimeline("Workflow ready for PR.");
        await _mcpAdapter.PublishStatusAsync("Workflow ready for PR.");
        string artifactDir = await WorkflowArtifactWriter.WriteAsync(state);
        state.AddTimeline($"Artifacts written to {artifactDir}");
        await _mcpAdapter.PublishPullRequestAsync(state, cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.PullRequestStatus))
        {
            state.AddTimeline($"PR status: {state.PullRequestStatus}");
        }
        if (!string.IsNullOrWhiteSpace(state.PullRequestUrl))
        {
            state.AddTimeline($"PR url: {state.PullRequestUrl}");
        }

        state.Stage = WorkflowStage.Done;
        state.AddTimeline("Workflow completed.");
        await _mcpAdapter.PublishStatusAsync("Workflow completed.");
        return state;
    }

    private async Task RunCompilationFixLoopAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (state.BuildValidation is not null
               && state.BuildValidation.Findings.Count > 0
               && attempt < _maxCompilationFixAttempts)
        {
            attempt++;
            state.Stage = WorkflowStage.Integrating;
            state.CompilationFixAllowedFiles = DetermineCompilationFixAllowedFiles(state);
            state.CompilationContractContext = BuildCompilationContractContext(
                state.RepoPath,
                state.CompilationFixAllowedFiles,
                state.BuildValidation?.Findings);
            state.AddTimeline($"Compilation fix attempt {attempt} started.");
            await _mcpAdapter.PublishStatusAsync($"Compilation fix attempt {attempt} started.");

            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            state.AddTimeline($"Compilation fix attempt {attempt} output generated.");

            var applyResult = await GeneratedFileApplier.ApplyAsync(state);
            if (applyResult.AppliedFiles.Count > 0)
            {
                state.AddTimeline($"Compilation fix applied files: {string.Join(", ", applyResult.AppliedFiles)}");
                CaptureRollbackChanges(rollbackChanges, applyResult.AppliedChanges);
            }
            else
            {
                state.AddTimeline("Compilation fix produced no applicable file changes.");
            }

            foreach (var rejected in applyResult.RejectedFiles)
            {
                state.AddTimeline($"Compilation fix rejected '{rejected.RelativePath}': {rejected.Reason}");
                state.ComplianceIssues.Add($"Compilation fix rejected '{rejected.RelativePath}': {rejected.Reason}");
            }

            state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
            if (state.BuildValidation.Findings.Count == 0)
            {
                state.AddTimeline($"Build passed after compilation fix attempt {attempt}.");
                await _mcpAdapter.PublishStatusAsync($"Build passed after compilation fix attempt {attempt}.");
                break;
            }

            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding after compilation fix {attempt}: [{finding.Severity}] {finding.Message}");
            }
            await _mcpAdapter.PublishStatusAsync($"Build still failing after compilation fix attempt {attempt}.");
        }

        if (ShouldAttemptTestQuarantine(state))
        {
            await TryQuarantineTestArtifactsAsync(state, rollbackChanges, cancellationToken);
            ApplyTestFailureReleasePolicy(state);
        }
    }

    private static bool ShouldAttemptTestQuarantine(WorkflowState state)
    {
        return state.BuildValidation is not null
               && state.BuildValidation.ProductionBuildPassed == true
               && state.BuildValidation.Findings.Count > 0
               && BuildFailureClassifier.IsOnlyTestFailures(state.BuildValidation.Findings);
    }

    private async Task<bool> TryQuarantineTestArtifactsAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        var testChanges = rollbackChanges.Values
            .Where(change => BuildFailureClassifier.IsTestArtifactPath(change.RelativePath))
            .ToList();
        if (testChanges.Count == 0)
        {
            return false;
        }

        await GeneratedFileApplier.RollbackAsync(state.RepoPath, testChanges);
        foreach (var change in testChanges)
        {
            rollbackChanges.Remove(change.RelativePath);
            string fileName = Path.GetFileName(change.RelativePath);
            if (fileName.EndsWith("RepositoryTests.cs", StringComparison.OrdinalIgnoreCase))
            {
                string entity = fileName[..^"RepositoryTests.cs".Length];
                if (!string.IsNullOrWhiteSpace(entity))
                {
                    state.DeferredTestEntities.Add(entity);
                }
            }
        }

        state.AddTimeline($"Quarantined {testChanges.Count} test artifact(s) with unresolved compile errors.");
        await _mcpAdapter.PublishStatusAsync($"Quarantined {testChanges.Count} failing test artifact(s); production code retained.");

        state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
        if (state.BuildValidation.Findings.Count > 0)
        {
            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding after test quarantine: [{finding.Severity}] {finding.Message}");
            }
        }
        else
        {
            state.AddTimeline("Build passed after quarantining failing test artifacts.");
        }

        return true;
    }

    private static void RefreshAutomatedComplianceAuditFindings(
        WorkflowState state,
        List<AgentFinding> llmOutputQualityFindings)
    {
        if (state.Audit is null)
        {
            return;
        }

        state.Audit.Findings.RemoveAll(IsAutomatedComplianceFinding);
        var refreshedCompliance = CollectComplianceFindings(state);
        refreshedCompliance.AddRange(llmOutputQualityFindings);
        state.Audit.Findings.AddRange(refreshedCompliance);
        if (state.BuildValidation is not null)
        {
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }

        ApplyTestFailureReleasePolicy(state);
    }

    private static bool IsAutomatedComplianceFinding(AgentFinding finding)
    {
        return finding.Message.StartsWith("Missing unit test for ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Missing repository interface for ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Duplicate index file detected", StringComparison.OrdinalIgnoreCase)
               || finding.Message.Contains("should implement I", StringComparison.OrdinalIgnoreCase)
               || finding.Message.Contains("should include", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Rejected generated file ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Rejected recovery file ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Deferred test gate:", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Deferred unit test generation for ", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyTestFailureReleasePolicy(WorkflowState state)
    {
        if (state.Audit is null || state.BuildValidation?.ProductionBuildPassed != true)
        {
            return;
        }

        var analysis = BuildFailureClassifier.Analyze(state.BuildValidation.Findings);
        if (!analysis.IsTestOnly && !analysis.HasTestFailures)
        {
            return;
        }

        var downgradedMessages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in state.Audit.Findings.ToList())
        {
            if (finding.Severity is not (FindingSeverity.High or FindingSeverity.Blocker))
            {
                continue;
            }

            if (BuildFailureClassifier.ClassifyMessage(finding.Message) != BuildFailureScope.Test
                && !finding.Message.Contains("Missing unit test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            state.Audit.Findings.Remove(finding);
            if (downgradedMessages.Add(finding.Message))
            {
                state.Audit.Findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Medium,
                    Message = $"Deferred test gate: {finding.Message}"
                });
            }
        }

        foreach (string entity in state.DeferredTestEntities)
        {
            string deferredMessage = $"Deferred unit test generation for {entity} repository after repeated test compile failures.";
            if (downgradedMessages.Add(deferredMessage))
            {
                state.Audit.Findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Medium,
                    Message = deferredMessage
                });
            }
        }
    }

    private static void CaptureRollbackChanges(
        Dictionary<string, AppliedFileChange> rollbackChanges,
        IReadOnlyList<AppliedFileChange> currentChanges)
    {
        foreach (var change in currentChanges)
        {
            if (!rollbackChanges.ContainsKey(change.RelativePath))
            {
                rollbackChanges[change.RelativePath] = change;
            }
        }
    }

    private static List<string> DetermineCompilationFixAllowedFiles(WorkflowState state)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in state.BuildValidation?.Findings ?? Enumerable.Empty<AgentFinding>())
        {
            foreach (var file in ExtractFilePathsFromBuildMessage(finding.Message, state.RepoPath))
            {
                files.Add(file);
            }
        }
        foreach (var issue in state.ComplianceIssues)
        {
            foreach (var file in ExtractFilePathsFromBuildMessage(issue, state.RepoPath))
            {
                files.Add(file);
            }
        }

        var declarationIndex = BuildTypeDeclarationIndex(state.RepoPath);
        ExpandWithBuildSymbolHints(state, files, declarationIndex);

        if (files.Count == 0)
        {
            foreach (var path in state.Backend?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
            foreach (var path in state.Frontend?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
            foreach (var path in state.Recovery?.ProposedFiles.Select(f => f.RelativePath) ?? Enumerable.Empty<string>())
            {
                files.Add(path.Replace('\\', '/'));
            }
        }

        ExpandWithContractDependencies(state.RepoPath, files, declarationIndex);
        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(80).ToList();
    }

    private static IEnumerable<string> ExtractFilePathsFromBuildMessage(string message, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Enumerable.Empty<string>();
        }

        var matches = Regex.Matches(message, @"([A-Za-z0-9_\-./\\]+\.cs)(?:\(\d+,\d+\))?");
        return matches
            .Select(match => NormalizeToRepoRelativePath(match.Groups[1].Value, repoPath))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToRepoRelativePath(string path, string repoPath)
    {
        string normalized = path.Replace('\\', '/');
        if (!Path.IsPathRooted(normalized))
        {
            return normalized.TrimStart('/');
        }

        string repoRoot = Path.GetFullPath(repoPath).Replace('\\', '/').TrimEnd('/');
        string absolute = Path.GetFullPath(path).Replace('\\', '/');
        if (absolute.StartsWith(repoRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return absolute[(repoRoot.Length + 1)..];
        }

        return normalized;
    }

    private static void ExpandWithContractDependencies(
        string repoPath,
        HashSet<string> files,
        Dictionary<string, List<string>>? declarationIndex = null)
    {
        declarationIndex ??= BuildTypeDeclarationIndex(repoPath);
        var queue = new Queue<(string RelativePath, int Depth)>(files.Select(path => (path, 0)));
        var visited = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        const int maxDepth = 2;
        const int maxTotalFiles = 120;

        while (queue.Count > 0 && files.Count < maxTotalFiles)
        {
            var (relativePath, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute) || !absolute.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            var referencedTypes = ExtractReferencedTypeNames(content);
            foreach (var typeName in referencedTypes)
            {
                if (!declarationIndex.TryGetValue(typeName, out var declaringFiles))
                {
                    continue;
                }

                foreach (var declaring in declaringFiles)
                {
                    if (visited.Contains(declaring))
                    {
                        continue;
                    }

                    visited.Add(declaring);
                    files.Add(declaring);
                    queue.Enqueue((declaring, depth + 1));
                    if (files.Count >= maxTotalFiles)
                    {
                        break;
                    }
                }

                if (files.Count >= maxTotalFiles)
                {
                    break;
                }
            }

            // Pull nearby contracts/helpers in the same directory (generic, bounded).
            string? directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                foreach (var sibling in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                             .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                         && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                             .Take(12))
                {
                    string relativeSibling = Path.GetRelativePath(repoPath, sibling).Replace('\\', '/');
                    if (visited.Contains(relativeSibling))
                    {
                        continue;
                    }
                    visited.Add(relativeSibling);
                    files.Add(relativeSibling);
                    queue.Enqueue((relativeSibling, depth + 1));
                    if (files.Count >= maxTotalFiles)
                    {
                        break;
                    }
                }
            }
        }
    }

    private static Dictionary<string, List<string>> BuildTypeDeclarationIndex(string repoPath)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            foreach (Match match in Regex.Matches(content, @"\b(class|interface|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)"))
            {
                string typeName = match.Groups[2].Value;
                if (!index.TryGetValue(typeName, out var list))
                {
                    list = new List<string>();
                    index[typeName] = list;
                }
                if (!list.Any(existing => existing.Equals(relative, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(relative);
                }
            }
        }

        return index;
    }

    private static HashSet<string> ExtractReferencedTypeNames(string content)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]*)\b"))
        {
            string token = match.Groups[1].Value;
            if (token is "Namespace" or "Class" or "Interface" or "Public" or "Private" or "Protected" or "Internal" or "Static")
            {
                continue;
            }
            referenced.Add(token);
        }

        foreach (Match match in Regex.Matches(content, @"\bI([A-Z][A-Za-z0-9_]*)\b"))
        {
            referenced.Add("I" + match.Groups[1].Value);
        }

        return referenced;
    }

    private static void ExpandWithBuildSymbolHints(
        WorkflowState state,
        HashSet<string> files,
        Dictionary<string, List<string>> declarationIndex)
    {
        foreach (var finding in state.BuildValidation?.Findings ?? Enumerable.Empty<AgentFinding>())
        {
            foreach (var symbol in ExtractMissingSymbolsFromBuildMessage(finding.Message))
            {
                if (!declarationIndex.TryGetValue(symbol, out var candidates))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    files.Add(candidate);
                }
            }
        }
    }

    private static IEnumerable<string> ExtractMissingSymbolsFromBuildMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Enumerable.Empty<string>();
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(message, @"name '([A-Za-z_][A-Za-z0-9_]*)' could not be found", RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }
        foreach (Match match in Regex.Matches(message, @"'([A-Za-z_][A-Za-z0-9_]*)' does not contain a definition for", RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        return symbols;
    }

    private static string BuildCompilationContractContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings = null)
    {
        var contextLines = new List<string>
        {
            "Use only these declared contracts and signatures."
        };

        AppendRepositoryTestExemplarContext(repoPath, allowedFiles, buildFindings, contextLines);

        foreach (var relative in allowedFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Take(24))
        {
            string absolute = Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            contextLines.Add($"File: {relative}");
            foreach (Match match in Regex.Matches(content, @"public\s+(?:interface|class)\s+[^{\r\n]+"))
            {
                contextLines.Add($"- {match.Value.Trim()}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^)]*\)"))
            {
                string signature = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {signature}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\{\s*get;\s*(set;)?\s*\}"))
            {
                string property = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {property}");
            }
        }

        if (contextLines.Count == 1)
        {
            return "- No explicit contract declarations were collected.";
        }

        string joined = string.Join('\n', contextLines);
        return joined.Length > 6000 ? joined[..6000] + "\n[contract context truncated]" : joined;
    }

    private static void AppendRepositoryTestExemplarContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings,
        List<string> contextLines)
    {
        bool hasTestTargets = allowedFiles.Any(BuildFailureClassifier.IsTestArtifactPath)
            || (buildFindings is not null && buildFindings.Any(f => BuildFailureClassifier.ClassifyMessage(f.Message) == BuildFailureScope.Test));
        if (!hasTestTargets)
        {
            return;
        }

        string? testsDir = TestCoverageAuditor.GetRepositoryTestsDirectory(repoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return;
        }

        string testsAbsoluteDir = Path.Combine(repoPath, testsDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return;
        }

        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, "*RepositoryTests.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return;
        }

        string exemplarRelative = Path.GetRelativePath(repoPath, exemplarPath).Replace('\\', '/');
        string exemplarContent = File.ReadAllText(exemplarPath);
        if (exemplarContent.Length > 2500)
        {
            exemplarContent = exemplarContent[..2500] + "\n// [exemplar truncated]";
        }

        contextLines.Add("Repository test exemplar (mirror structure exactly; valid C# only):");
        contextLines.Add($"File: {exemplarRelative}");
        contextLines.Add(exemplarContent);
    }

    private static List<AgentFinding> CollectComplianceFindings(WorkflowState state)
    {
        var findings = ValidateRepositoryContracts(state);
        findings.AddRange(ValidatePathConventions(state));
        findings.AddRange(TestCoverageAuditor.ValidateMissingTests(state));
        return findings;
    }

    private static List<AgentFinding> ValidateRepositoryContracts(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var backendFiles = GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(backendFiles.Select(f => f.RelativePath.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        var interfaceConvention = DetectRepositoryInterfaceConvention(state.RepoPath);
        var layerProfiles = LayerConventionProfileBuilder.Build(state.RepoPath);
        var repositoryProfile = layerProfiles.Repository;

        foreach (var repoImplPath in proposedPaths.Where(IsRepositoryImplementationPath))
        {
            string fileName = Path.GetFileName(repoImplPath);
            string entity = fileName[..^"Repository.cs".Length];
            if (string.IsNullOrWhiteSpace(entity))
            {
                continue;
            }

            string expectedInterfaceName = $"I{entity}Repository.cs";
            string expectedImplementationName = $"{entity}Repository.cs";
            bool interfaceInProposed = proposedPaths.Any(path => Path.GetFileName(path).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase));
            string? interfaceFilePathInRepo = Directory
                .EnumerateFiles(state.RepoPath, expectedInterfaceName, SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase));
            bool interfaceInRepo = !string.IsNullOrWhiteSpace(interfaceFilePathInRepo);

            if (!interfaceInProposed && !interfaceInRepo)
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"Missing repository interface for {entity}: expected {expectedInterfaceName} under Interfaces."
                });
                continue;
            }

            string? interfaceContent = backendFiles
                .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase))
                ?.Content;
            if (string.IsNullOrWhiteSpace(interfaceContent) && interfaceInRepo && !string.IsNullOrWhiteSpace(interfaceFilePathInRepo))
            {
                interfaceContent = File.ReadAllText(interfaceFilePathInRepo);
            }
            if (!string.IsNullOrWhiteSpace(interfaceContent))
            {
                if (interfaceConvention.RequireInheritanceClause && !interfaceContent.Contains(':'))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"Interface {expectedInterfaceName} should define an inheritance clause to match existing repository interface style."
                    });
                }

                foreach (var token in interfaceConvention.RequiredBaseTokens)
                {
                    if (!InterfaceContainsToken(interfaceContent, token))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"Interface {expectedInterfaceName} should include base token '{token}' to match repository contracts."
                        });
                    }
                }
            }

            string? implementationContent = backendFiles
                .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedImplementationName, StringComparison.OrdinalIgnoreCase))
                ?.Content;
            if (string.IsNullOrWhiteSpace(implementationContent))
            {
                string? implementationPathInRepo = Directory
                    .EnumerateFiles(state.RepoPath, expectedImplementationName, SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains("/Interfaces/", StringComparison.OrdinalIgnoreCase)
                                         && path.Contains("Repository", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(implementationPathInRepo))
                {
                    implementationContent = File.ReadAllText(implementationPathInRepo);
                }
            }

            if (!string.IsNullOrWhiteSpace(implementationContent))
            {
                if (implementationContent.Contains("// Implement", StringComparison.OrdinalIgnoreCase)
                    || implementationContent.Contains("// TODO", StringComparison.OrdinalIgnoreCase)
                    || implementationContent.Contains("throw new NotImplementedException", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{expectedImplementationName} contains placeholder implementation markers."
                    });
                }

                string? classLine = implementationContent
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));

                if (repositoryProfile is not null && !string.IsNullOrWhiteSpace(classLine))
                {
                    if (repositoryProfile.RequireInheritanceClause && !classLine.Contains(':'))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"{expectedImplementationName} should include an inheritance clause to match repository style."
                        });
                    }

                    if (repositoryProfile.RequireMatchingRoleInterface
                        && !classLine.Contains($"I{entity}Repository", StringComparison.Ordinal))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"{expectedImplementationName} should implement I{entity}Repository based on current conventions."
                        });
                    }

                    foreach (var token in repositoryProfile.RequiredInheritedTypeTokens)
                    {
                        if (!ClassLineMatchesToken(classLine, token))
                        {
                            findings.Add(new AgentFinding
                            {
                                Severity = FindingSeverity.High,
                                Message = $"{expectedImplementationName} should include inherited token '{token}' based on repository conventions."
                            });
                        }
                    }

                    if (repositoryProfile.RequireBaseConstructorCall && !implementationContent.Contains("base(", StringComparison.Ordinal))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"{expectedImplementationName} should include constructor base(...) call to match repository conventions."
                        });
                    }

                    foreach (var paramType in repositoryProfile.RequiredConstructorParamTypes)
                    {
                        if (!implementationContent.Contains(paramType, StringComparison.Ordinal))
                        {
                            findings.Add(new AgentFinding
                            {
                                Severity = FindingSeverity.High,
                                Message = $"{expectedImplementationName} should include constructor dependency '{paramType}' based on repository conventions."
                            });
                        }
                    }
                }
            }
        }

        return findings;
    }

    private static List<AgentFinding> ValidatePathConventions(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var backendFiles = GetAllProposedFiles(state);
        string? webApiControllersDir = DetectCanonicalWebApiControllersDir(state.RepoPath);
        string? repoIndexesDir = DetectCanonicalRepositoryIndexesDir(state.RepoPath);

        foreach (var file in backendFiles)
        {
            string path = file.RelativePath.Replace('\\', '/');
            string fileName = Path.GetFileName(path);
            if (fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(webApiControllersDir)
                && !path.StartsWith(webApiControllersDir + "/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals(webApiControllersDir, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"{fileName} should be generated under {webApiControllersDir}, not {path}."
                });
            }

            if (fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(repoIndexesDir)
                && !path.StartsWith(repoIndexesDir + "/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals(repoIndexesDir, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"{fileName} should be generated under {repoIndexesDir}, not {path}."
                });
            }
        }

        var allIndexFiles = Directory
            .EnumerateFiles(state.RepoPath, "*Index.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var duplicatedIndexNames = allIndexFiles
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var duplicated in duplicatedIndexNames)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Duplicate index file detected in multiple folders: {duplicated}. Keep only canonical index path."
            });
        }

        return findings;
    }

    private static bool IsRepositoryImplementationPath(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase);
    }

    private static List<GeneratedFile> GetAllProposedFiles(WorkflowState state)
    {
        return (state.Backend?.ProposedFiles ?? new List<GeneratedFile>())
            .Concat(state.Recovery?.ProposedFiles ?? new List<GeneratedFile>())
            .ToList();
    }

    private static bool HasBlockingFindings(IEnumerable<AgentFinding> findings)
    {
        return findings.Any(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker);
    }

    private static RepositoryInterfaceConvention DetectRepositoryInterfaceConvention(string repoPath)
    {
        var interfaceFiles = Directory
            .EnumerateFiles(repoPath, "I*Repository.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (interfaceFiles.Count == 0)
        {
            return new RepositoryInterfaceConvention(
                RequireInheritanceClause: true,
                RequiredBaseTokens: Array.Empty<string>());
        }

        int withAnyBaseClause = 0;
        var baseTokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in interfaceFiles)
        {
            string? declaration = File.ReadLines(file)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("public interface ", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(declaration))
            {
                continue;
            }

            if (declaration.Contains(':'))
            {
                withAnyBaseClause++;
                string inheritance = declaration.Split(':', 2)[1];
                foreach (var token in inheritance.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => NormalizeGenericToken(x.Trim())))
                {
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }
                    baseTokenCounts[token] = baseTokenCounts.TryGetValue(token, out int count) ? count + 1 : 1;
                }
            }
        }

        int threshold = Math.Max(2, (int)Math.Ceiling(interfaceFiles.Count * 0.6));
        var requiredTokens = baseTokenCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new RepositoryInterfaceConvention(
            RequireInheritanceClause: withAnyBaseClause > 0,
            RequiredBaseTokens: requiredTokens);
    }

    private static string? DetectCanonicalWebApiControllersDir(string repoPath)
    {
        return DetectCanonicalDirectoryForFileSuffix(repoPath, "Controller.cs", "Controllers");
    }

    private static string? DetectCanonicalRepositoryIndexesDir(string repoPath)
    {
        return DetectCanonicalDirectoryForFileSuffix(repoPath, "Index.cs", "Indexes")
               ?? DetectCanonicalDirectoryForFileSuffix(repoPath, "Index.cs", "Index");
    }

    private static string? DetectCanonicalDirectoryForFileSuffix(
        string repoPath,
        string fileSuffix,
        string? preferredDirectoryName = null)
    {
        var matchingFiles = Directory.EnumerateFiles(repoPath, $"*{fileSuffix}", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchingFiles.Count == 0)
        {
            return null;
        }

        var grouped = matchingFiles
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => !string.IsNullOrWhiteSpace(relative))
            .GroupBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Directory = group.Key,
                Count = group.Count(),
                IsPreferred = !string.IsNullOrWhiteSpace(preferredDirectoryName)
                              && group.Key.EndsWith("/" + preferredDirectoryName, StringComparison.OrdinalIgnoreCase)
                              || (!string.IsNullOrWhiteSpace(preferredDirectoryName)
                                  && group.Key.Equals(preferredDirectoryName, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(entry => entry.Count)
            .ThenByDescending(entry => entry.IsPreferred)
            .ThenBy(entry => entry.Directory.Length)
            .ThenBy(entry => entry.Directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped.FirstOrDefault()?.Directory;
    }

    private static bool InterfaceContainsToken(string interfaceContent, string normalizedToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return true;
        }

        string normalizedContent = NormalizeGenericToken(interfaceContent);
        return normalizedContent.Contains(normalizedToken, StringComparison.Ordinal);
    }

    private static bool ClassLineMatchesToken(string classLine, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        string normalizedLine = NormalizeGenericToken(classLine);
        return normalizedLine.Contains(token, StringComparison.Ordinal);
    }

    private static string NormalizeGenericToken(string value)
    {
        // Replace concrete generic arguments with <T> so conventions remain generic.
        return Regex.Replace(value, @"<\s*[A-Za-z_][A-Za-z0-9_]*\s*>", "<T>");
    }

    private readonly record struct RepositoryInterfaceConvention(
        bool RequireInheritanceClause,
        IReadOnlyList<string> RequiredBaseTokens);
}

