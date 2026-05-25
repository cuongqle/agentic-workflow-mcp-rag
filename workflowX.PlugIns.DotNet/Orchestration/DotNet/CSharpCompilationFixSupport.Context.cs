using System.Text;
using workflowX.Configuration;
using workflowX.Infrastructure;

static partial class CSharpCompilationFixSupport
{
    public static (string Context, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> OmittedPaths) BuildContext(
        WorkflowState state,
        CompilationFixContextOptions? options = null)
    {
        options ??= new CompilationFixContextOptions();
        string repoPath = state.RepoPath;
        RepoContract contract = DotNetRepoContractSupport.GetContract(state);
        var allowedPaths = state.CompilationFixAllowedFiles.Count > 0
            ? state.CompilationFixAllowedFiles
            : CSharpCompilationFixSupport.DetermineAllowedFiles(state);
        var pathSet = new HashSet<string>(allowedPaths, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<AgentFinding> buildFindings = state.BuildValidation is not null
            ? state.BuildValidation.Findings
            : Array.Empty<AgentFinding>();
        var errorPaths = BuildFailureClassifier.CollectSourcePathsFromFindings(buildFindings, repoPath);
        AddContractExemplarPaths(pathSet, errorPaths, contract, repoPath);

        LayerConventionProfiles conventions = contract.LayerConventions;
        HashSet<string> mandatoryPaths = BuildMandatoryPaths(
            repoPath,
            errorPaths,
            contract,
            conventions);

        var optionalPaths = pathSet
            .Where(path => !mandatoryPaths.Contains(path))
            .ToList();
        var orderedMandatory = RankPaths(state, mandatoryPaths.ToList(), errorPaths, contract)
            .ToList();
        var orderedOptional = RankPaths(state, optionalPaths, errorPaths, contract).ToList();
        var ordered = orderedMandatory.Concat(orderedOptional).ToList();

        var attached = new List<string>();
        var omitted = new List<string>();
        var sb = new StringBuilder();
        int usedChars = 0;
        int optionalAttached = 0;

        foreach (string relativePath in ordered)
        {
            string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            bool isMandatory = mandatoryPaths.Contains(relativePath);
            string content = File.ReadAllText(absolute);
            string block = FormatFileBlock(relativePath, content);

            if (!isMandatory && ShouldOmitOptional(block.Length, usedChars, optionalAttached, options))
            {
                omitted.Add(relativePath);
                continue;
            }

            sb.Append(block);
            usedChars += block.Length;
            attached.Add(relativePath);
            if (!isMandatory)
            {
                optionalAttached++;
            }
        }

        if (omitted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"[Lower-priority allowed paths not inlined ({omitted.Count}): {string.Join(", ", omitted)}]");
            sb.AppendLine(
                "[All build-error files and same-directory sources above are included. Paths still editable per Allowed files list.]");
        }

        if (attached.Count == 0)
        {
            return ("(No exemplar files could be loaded from disk.)", attached, omitted);
        }

        return (sb.ToString().TrimEnd(), attached, omitted);
    }

    private static bool ShouldOmitOptional(int blockLength, int usedChars, int optionalAttached, CompilationFixContextOptions options)
    {
        if (options.MaxOptionalFiles > 0 && optionalAttached >= options.MaxOptionalFiles)
        {
            return true;
        }

        if (options.MaxTotalChars > 0 && usedChars > 0 && usedChars + blockLength > options.MaxTotalChars)
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> BuildMandatoryPaths(
        string repoPath,
        HashSet<string> errorPaths,
        RepoContract contract,
        LayerConventionProfiles conventions)
    {
        var mandatory = new HashSet<string>(errorPaths, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(contract.Entity?.ExemplarRelativePath))
        {
            mandatory.Add(contract.Entity.ExemplarRelativePath);
        }

        foreach (string errorPath in errorPaths)
        {
            string? layerExemplar = FindLayerExemplarInDirectory(repoPath, errorPath, conventions);
            if (!string.IsNullOrWhiteSpace(layerExemplar))
            {
                mandatory.Add(layerExemplar);
            }

            AddErrorDirectorySiblings(repoPath, errorPath, mandatory);
        }

        return mandatory;
    }

    private static void AddErrorDirectorySiblings(string repoPath, string relativePath, HashSet<string> mandatory)
    {
        string? directory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string absoluteDir = Path.Combine(repoPath, directory);
        if (!Directory.Exists(absoluteDir))
        {
            return;
        }

        foreach (string sibling in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            mandatory.Add(Path.GetRelativePath(repoPath, sibling).Replace('\\', '/'));
        }
    }

    private static IEnumerable<string> RankPaths(
        WorkflowState state,
        IReadOnlyList<string> allowed,
        HashSet<string> errorPaths,
        RepoContract contract)
    {
        LayerConventionProfiles conventions = contract.LayerConventions;
        return allowed
            .Select(path => new
            {
                Path = path,
                Score = ScorePath(path, errorPaths, contract, conventions, state.RepoPath)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path);
    }

    private static int ScorePath(
        string relativePath,
        HashSet<string> errorPaths,
        RepoContract contract,
        LayerConventionProfiles conventions,
        string repoPath)
    {
        int score = 0;
        if (errorPaths.Contains(relativePath))
        {
            score += 100;
        }

        if (contract.Entity is not null)
        {
            if (relativePath.Equals(contract.Entity.ExemplarRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                score += 90;
            }

            if (relativePath.StartsWith(contract.Entity.CanonicalDirectory + "/", StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }
        }

        if (conventions.ResolveByPath(relativePath) is LayerConventionProfile profile
            && !string.IsNullOrWhiteSpace(profile.CanonicalDirectory)
            && relativePath.StartsWith(profile.CanonicalDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        foreach (string errorPath in errorPaths)
        {
            string? errorDir = Path.GetDirectoryName(errorPath.Replace('/', Path.DirectorySeparatorChar));
            string? fileDir = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(errorDir)
                && !string.IsNullOrWhiteSpace(fileDir)
                && errorDir.Equals(fileDir, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
                break;
            }
        }

        if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (relativePath.Contains("/Properties/AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        string? layerExemplar = FindLayerExemplarInDirectory(repoPath, relativePath, conventions);
        if (!string.IsNullOrWhiteSpace(layerExemplar)
            && layerExemplar.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 85;
        }

        return score;
    }

    private static string? FindLayerExemplarInDirectory(
        string repoPath,
        string relativePath,
        LayerConventionProfiles conventions)
    {
        LayerConventionProfile? profile = conventions.ResolveByPath(relativePath);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CanonicalDirectory))
        {
            return null;
        }

        string directory = Path.Combine(repoPath, profile.CanonicalDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directory, $"*{profile.FileSuffix}", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith('I'))
            .Where(path => !path.Contains("Test", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .FirstOrDefault(path => !path.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddContractExemplarPaths(
        HashSet<string> paths,
        HashSet<string> errorPaths,
        RepoContract contract,
        string repoPath)
    {
        if (!string.IsNullOrWhiteSpace(contract.Entity?.ExemplarRelativePath))
        {
            paths.Add(contract.Entity.ExemplarRelativePath);
        }

        LayerConventionProfiles conventions = contract.LayerConventions;
        foreach (string errorPath in errorPaths)
        {
            string? layerExemplar = FindLayerExemplarInDirectory(repoPath, errorPath, conventions);
            if (!string.IsNullOrWhiteSpace(layerExemplar))
            {
                paths.Add(layerExemplar);
            }
        }
    }

    private static string FormatFileBlock(string relativePath, string content) =>
        $"\n--- FILE: {relativePath} ---\n{content.TrimEnd()}\n--- END {relativePath} ---\n";
}
