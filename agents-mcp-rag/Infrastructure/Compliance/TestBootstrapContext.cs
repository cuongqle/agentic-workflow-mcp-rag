using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Discovers test composition-root types from the repo and validates/normalizes how tests resolve dependencies.
/// </summary>
internal static class TestBootstrapContext
{
    private static readonly Regex PublicStaticMethodRegex = new(
        @"public\s+static\s+(?:[\w<>\[\],\s\?]+\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex PublicStaticGenericMethodRegex = new(
        @"public\s+static\s+[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*<",
        RegexOptions.Compiled);

    private static readonly Regex PrivateMemberRegex = new(
        @"(?:private|protected)\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|;)",
        RegexOptions.Compiled);

    private static readonly Regex TestBootstrapUsageRegex = new(
        @"\b([A-Z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*(?:<|\()",
        RegexOptions.Compiled);

    private static readonly Regex ServiceProviderAccessRegex = new(
        @"\b([A-Za-z_][A-Za-z0-9_]*)\.ServiceProvider\.Get(?:Required)?Service\s*<([^>]+)>\s*\(\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex PublicPropertyRegex = new(
        @"public\s+([\w<>\.?\[\]]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NewEntityInitializerRegex = new(
        @"new\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{",
        RegexOptions.Compiled);

    private static readonly string[] TemporalTypeNames =
    [
        "DateTime",
        "DateTimeOffset",
        "DateOnly",
        "TimeOnly"
    ];

    private static readonly string[] BootstrapPathTokens =
    {
        "Bootstrap",
        "Bootstrappers",
        "CompositionRoot",
        "TestFixture",
        "TestInfrastructure"
    };

    internal static string? BuildContext(string repoPath)
    {
        BootstrapInfo? bootstrap = DiscoverBootstrap(repoPath);
        if (bootstrap is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Test bootstrap resolution (use public API only — mirror exemplar Setup lines):");
        string? diScope = BootstrapRegistrationScope.BuildContext(repoPath);
        if (!string.IsNullOrWhiteSpace(diScope))
        {
            sb.AppendLine(diScope);
        }

        string? packageContext = ProjectPackageAuditor.BuildTestPackageContext(repoPath);
        if (!string.IsNullOrWhiteSpace(packageContext))
        {
            sb.AppendLine(packageContext);
        }

        sb.AppendLine($"- Bootstrap type: {bootstrap.ClassName} ({bootstrap.Namespace})");
        if (bootstrap.PublicMethods.Count > 0)
        {
            sb.AppendLine(
                $"- Public methods: {string.Join(", ", bootstrap.PublicMethods.OrderBy(m => m, StringComparer.Ordinal))}");
        }

        foreach (string member in bootstrap.PrivateMembers)
        {
            sb.AppendLine($"- Do NOT access {bootstrap.ClassName}.{member} (not public)");
        }

        string? setupExemplar = FindSetupExemplar(repoPath, bootstrap.ClassName);
        if (!string.IsNullOrWhiteSpace(setupExemplar))
        {
            sb.AppendLine("- Exemplar [TestInitialize] / Setup (copy this pattern):");
            foreach (string line in setupExemplar.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine($"  {line.Trim()}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(bootstrap.ResolveMethod))
        {
            sb.AppendLine(
                $"- Resolve dependencies via {bootstrap.ClassName}.{bootstrap.ResolveMethod}<T>() (never via private ServiceProvider).");
        }

        string? propertyContext = BuildEntityPropertyContext(repoPath);
        if (!string.IsNullOrWhiteSpace(propertyContext))
        {
            sb.AppendLine(propertyContext);
        }

        return sb.ToString();
    }

    internal static string? BuildEntityPropertyContext(string repoPath)
    {
        var catalog = BuildEntityPropertyCatalog(repoPath);
        if (catalog.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Entity/model property types for tests (use these CLR types — do not assign or compare string literals to non-string properties):");
        foreach (var (typeName, properties) in catalog.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            string props = string.Join(", ", properties.Select(p => $"{p.Key}: {p.Value}"));
            sb.AppendLine($"- {typeName}: {props}");
        }

        string? temporalPattern = DiscoverTemporalLiteralPattern(repoPath);
        if (!string.IsNullOrWhiteSpace(temporalPattern))
        {
            sb.AppendLine($"- Temporal literals in sibling *Tests.cs: {temporalPattern}");
        }

        return sb.ToString();
    }

    internal static string NormalizeTestLiteralTypes(string content, string repoPath)
    {
        var catalog = BuildEntityPropertyCatalog(repoPath);
        if (catalog.Count == 0)
        {
            return content;
        }

        var referencedTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in NewEntityInitializerRegex.Matches(content))
        {
            referencedTypes.Add(match.Groups[1].Value);
        }

        foreach (string typeName in referencedTypes)
        {
            if (!catalog.TryGetValue(typeName, out var properties))
            {
                continue;
            }

            foreach (var (propertyName, propertyType) in properties)
            {
                if (!IsTemporalType(propertyType))
                {
                    continue;
                }

                content = NormalizeTemporalAssignments(content, propertyName, propertyType);
                content = NormalizeTemporalComparisons(content, propertyName, propertyType);
                content = NormalizeTemporalAssertEquals(content, propertyName, propertyType);
            }
        }

        return EnsureGlobalizationUsing(content);
    }

    internal static bool TryValidateTestLiteralTypes(string content, string repoPath, out string reason)
    {
        reason = string.Empty;
        var catalog = BuildEntityPropertyCatalog(repoPath);
        foreach (var (typeName, properties) in catalog)
        {
            if (!content.Contains(typeName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (propertyName, propertyType) in properties)
            {
                if (!IsTemporalType(propertyType))
                {
                    continue;
                }

                if (HasStringLiteralAssignment(content, propertyName)
                    || HasStringLiteralMemberComparison(content, propertyName)
                    || HasStringLiteralAssertEquals(content, propertyName))
                {
                    reason =
                        $"Test uses string literals for {typeName}.{propertyName} ({propertyType}). "
                        + $"Use {DescribeTemporalLiteralPattern(propertyType, repoPath)} instead of quoted strings.";
                    return false;
                }
            }
        }

        return true;
    }

    internal static string NormalizeResolutionAccess(string content, string repoPath)
    {
        BootstrapInfo? bootstrap = DiscoverBootstrap(repoPath);
        if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrap.ResolveMethod))
        {
            return content;
        }

        return ServiceProviderAccessRegex.Replace(
            content,
            match => $"{match.Groups[1].Value}.{bootstrap.ResolveMethod}<{match.Groups[2].Value}>()");
    }

    internal static bool TryValidateTestResolution(string content, string repoPath, out string reason)
    {
        reason = string.Empty;
        BootstrapInfo? bootstrap = DiscoverBootstrap(repoPath);
        if (bootstrap is null)
        {
            return true;
        }

        if (ServiceProviderAccessRegex.IsMatch(content))
        {
            string resolveHint = string.IsNullOrWhiteSpace(bootstrap.ResolveMethod)
                ? "the public generic resolver on the bootstrap type"
                : $"{bootstrap.ClassName}.{bootstrap.ResolveMethod}<T>()";
            reason =
                $"Test code must not access {bootstrap.ClassName}.ServiceProvider. Use {resolveHint} after the bootstrap init call shown in existing *Tests.cs exemplars.";
            return false;
        }

        foreach (string member in bootstrap.PrivateMembers)
        {
            if (content.Contains($"{bootstrap.ClassName}.{member}", StringComparison.Ordinal))
            {
                reason =
                    $"Test code must not access private bootstrap member {bootstrap.ClassName}.{member}. Use public methods only ({string.Join(", ", bootstrap.PublicMethods)}).";
                return false;
            }
        }

        return true;
    }

    private static string? FindSetupExemplar(string repoPath, string bootstrapClassName)
    {
        var conventions = TestCoverageAuditor.DiscoverTestConventions(repoPath);
        foreach (var convention in conventions)
        {
            string testsDir = Path.Combine(
                repoPath,
                convention.TestDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(testsDir))
            {
                continue;
            }

            foreach (string testFile in Directory.EnumerateFiles(testsDir, "*Tests.cs", SearchOption.TopDirectoryOnly))
            {
                var setupLines = ExtractSetupLines(File.ReadAllText(testFile), bootstrapClassName);
                if (setupLines.Count > 0)
                {
                    return string.Join(Environment.NewLine, setupLines);
                }
            }
        }

        return null;
    }

    private static List<string> ExtractSetupLines(string content, string bootstrapClassName)
    {
        var lines = new List<string>();
        bool inSetup = false;
        foreach (string line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.Contains("TestInitialize", StringComparison.Ordinal)
                || trimmed.Contains("void Setup", StringComparison.Ordinal)
                || trimmed.Contains("void SetUp", StringComparison.Ordinal))
            {
                inSetup = true;
                continue;
            }

            if (!inSetup)
            {
                continue;
            }

            if (trimmed.StartsWith('[')
                || (trimmed.StartsWith("public void", StringComparison.Ordinal) && !trimmed.Contains("Setup", StringComparison.Ordinal)))
            {
                if (lines.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (trimmed is "{" or "}")
            {
                if (trimmed == "}")
                {
                    break;
                }

                continue;
            }

            if (trimmed.Contains($"{bootstrapClassName}.", StringComparison.Ordinal)
                || (trimmed.Contains('_') && trimmed.Contains('=')))
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    private static BootstrapInfo? DiscoverBootstrap(string repoPath)
    {
        var referencedTypes = CollectBootstrapTypesReferencedInTests(repoPath);
        BootstrapInfo? best = null;
        int bestScore = 0;

        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (absolute.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || absolute.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            string content = File.ReadAllText(absolute);
            string? className = Regex.Match(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            int score = ScoreBootstrapCandidate(relative, content, className, referencedTypes);
            if (score <= 0)
            {
                continue;
            }

            var publicMethods = PublicStaticMethodRegex.Matches(content)
                .Select(m => m.Groups[1].Value)
                .Where(name => name is not ("if" or "for" or "while"))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (publicMethods.Count == 0)
            {
                continue;
            }

            string? resolveMethod = InferResolveMethod(className, content, repoPath, referencedTypes);
            var privateMembers = PrivateMemberRegex.Matches(content)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string? ns = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal))
                ?.Substring("namespace ".Length)
                .Trim();

            if (score > bestScore)
            {
                bestScore = score;
                best = new BootstrapInfo(className, ns ?? string.Empty, publicMethods, privateMembers, resolveMethod);
            }
        }

        return best;
    }

    private static int ScoreBootstrapCandidate(
        string relativePath,
        string content,
        string className,
        IReadOnlySet<string> referencedTypes)
    {
        int score = 0;
        if (referencedTypes.Contains(className))
        {
            score += 12;
        }

        if (BootstrapPathTokens.Any(token => relativePath.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            score += 6;
        }

        if (content.Contains("ServiceCollection", StringComparison.Ordinal)
            || content.Contains("AddScoped", StringComparison.Ordinal)
            || content.Contains("AddSingleton", StringComparison.Ordinal)
            || content.Contains("BuildServiceProvider", StringComparison.Ordinal))
        {
            score += 4;
        }

        if (PublicStaticGenericMethodRegex.IsMatch(content))
        {
            score += 3;
        }

        if (relativePath.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static HashSet<string> CollectBootstrapTypesReferencedInTests(string repoPath)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            if (!BuildFailureClassifier.IsTestArtifactPath(relative))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            foreach (Match match in TestBootstrapUsageRegex.Matches(content))
            {
                types.Add(match.Groups[1].Value);
            }
        }

        return types;
    }

    private static string? InferResolveMethod(
        string className,
        string bootstrapContent,
        string repoPath,
        IReadOnlySet<string> referencedTypes)
    {
        var usageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(absolute);
            foreach (Match match in Regex.Matches(
                         content,
                         $@"\b{Regex.Escape(className)}\.([A-Za-z_][A-Za-z0-9_]*)\s*<",
                         RegexOptions.None))
            {
                string method = match.Groups[1].Value;
                usageCounts[method] = usageCounts.TryGetValue(method, out int count) ? count + 1 : 1;
            }
        }

        if (usageCounts.Count > 0)
        {
            return usageCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        foreach (Match match in PublicStaticGenericMethodRegex.Matches(bootstrapContent))
        {
            return match.Groups[1].Value;
        }

        return referencedTypes.Contains(className)
            ? null
            : null;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildEntityPropertyCatalog(string repoPath)
    {
        var catalog = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (absolute.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || absolute.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            if (!relative.Contains("Entit", StringComparison.OrdinalIgnoreCase)
                && !relative.Contains("/Models/", StringComparison.OrdinalIgnoreCase)
                && !relative.Contains("/Model/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            foreach (Match classMatch in Regex.Matches(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b"))
            {
                string typeName = classMatch.Groups[1].Value;
                if (!catalog.ContainsKey(typeName))
                {
                    catalog[typeName] = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                foreach (Match prop in PublicPropertyRegex.Matches(content))
                {
                    catalog[typeName][prop.Groups[2].Value] = prop.Groups[1].Value.Trim();
                }
            }
        }

        return catalog;
    }

    private static string? DiscoverTemporalLiteralPattern(string repoPath)
    {
        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(absolute);
            if (content.Contains("DateTime.UtcNow", StringComparison.Ordinal))
            {
                return "DateTime.UtcNow / DateTime.Parse(..., CultureInfo.InvariantCulture)";
            }

            if (content.Contains("DateTime.Parse", StringComparison.Ordinal))
            {
                return "DateTime.Parse(..., CultureInfo.InvariantCulture)";
            }

            if (content.Contains("DateTimeOffset.", StringComparison.Ordinal))
            {
                return "DateTimeOffset.Parse(..., CultureInfo.InvariantCulture)";
            }
        }

        return "DateTime.Parse(..., CultureInfo.InvariantCulture)";
    }

    private static string DescribeTemporalLiteralPattern(string propertyType, string repoPath) =>
        DiscoverTemporalLiteralPattern(repoPath) ?? BuildParseExpression(propertyType, "value") ?? propertyType;

    private static bool IsTemporalType(string propertyType)
    {
        string core = propertyType.TrimEnd('?');
        return TemporalTypeNames.Contains(core, StringComparer.Ordinal);
    }

    private static string NormalizeTemporalAssignments(string content, string propertyName, string propertyType)
    {
        string? parseExpr = BuildParseExpression(propertyType, "$1");
        if (parseExpr is null)
        {
            return content;
        }

        return Regex.Replace(
            content,
            $@"\b{Regex.Escape(propertyName)}\s*=\s*""([^""]+)""",
            $"{propertyName} = {parseExpr}",
            RegexOptions.None);
    }

    private static string NormalizeTemporalComparisons(string content, string propertyName, string propertyType)
    {
        string? parseExpr = BuildParseExpression(propertyType, "$2");
        if (parseExpr is null)
        {
            return content;
        }

        return Regex.Replace(
            content,
            $@"\.{Regex.Escape(propertyName)}\s*==\s*""([^""]+)""",
            $".{propertyName} == {parseExpr}",
            RegexOptions.None);
    }

    private static string NormalizeTemporalAssertEquals(string content, string propertyName, string propertyType)
    {
        string? parseExpr = BuildParseExpression(propertyType, "$1");
        if (parseExpr is null)
        {
            return content;
        }

        return Regex.Replace(
            content,
            $@"Assert\.AreEqual\s*\(\s*""([^""]+)""\s*,\s*([^,)]+?\.{Regex.Escape(propertyName)})\s*\)",
            $"Assert.AreEqual({parseExpr}, $2)",
            RegexOptions.None);
    }

    private static bool HasStringLiteralAssignment(string content, string propertyName) =>
        Regex.IsMatch(content, $@"\b{Regex.Escape(propertyName)}\s*=\s*""[^""]+""");

    private static bool HasStringLiteralMemberComparison(string content, string propertyName) =>
        Regex.IsMatch(content, $@"\.{Regex.Escape(propertyName)}\s*==\s*""[^""]+""");

    private static bool HasStringLiteralAssertEquals(string content, string propertyName) =>
        Regex.IsMatch(content, $@"Assert\.AreEqual\s*\(\s*""[^""]+""\s*,\s*[^,)]+?\.{Regex.Escape(propertyName)}\s*\)");

    private static string? BuildParseExpression(string propertyType, string literalGroup)
    {
        string core = propertyType.TrimEnd('?');
        return core switch
        {
            "DateTime" => $"DateTime.Parse(\"{literalGroup}\", CultureInfo.InvariantCulture)",
            "DateTimeOffset" => $"DateTimeOffset.Parse(\"{literalGroup}\", CultureInfo.InvariantCulture)",
            "DateOnly" => $"DateOnly.Parse(\"{literalGroup}\", CultureInfo.InvariantCulture)",
            "TimeOnly" => $"TimeOnly.Parse(\"{literalGroup}\", CultureInfo.InvariantCulture)",
            _ => null
        };
    }

    private static string EnsureGlobalizationUsing(string content)
    {
        if (!content.Contains("CultureInfo.InvariantCulture", StringComparison.Ordinal))
        {
            return content;
        }

        if (content.Contains("using System.Globalization", StringComparison.Ordinal))
        {
            return content;
        }

        int insertAt = 0;
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("using ", StringComparison.Ordinal))
            {
                insertAt = i + 1;
            }
            else if (insertAt > 0)
            {
                break;
            }
        }

        lines.Insert(insertAt, "using System.Globalization;");
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record BootstrapInfo(
        string ClassName,
        string Namespace,
        IReadOnlyList<string> PublicMethods,
        IReadOnlyList<string> PrivateMembers,
        string? ResolveMethod);
}
