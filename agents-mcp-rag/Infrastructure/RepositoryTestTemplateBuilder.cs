namespace agents_mcp_rag.Infrastructure;

internal static class RepositoryTestTemplateBuilder
{
    public static bool TryBuildFromExemplar(string repoPath, string entityName, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return false;
        }

        string? testsDir = TestCoverageAuditor.GetRepositoryTestsDirectory(repoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return false;
        }

        string testsAbsoluteDir = Path.Combine(repoPath, testsDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return false;
        }

        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, "*RepositoryTests.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => !Path.GetFileName(path).Equals($"{entityName}RepositoryTests.cs", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return false;
        }

        string exemplarEntity = Path.GetFileNameWithoutExtension(exemplarPath);
        if (exemplarEntity.EndsWith("RepositoryTests", StringComparison.Ordinal))
        {
            exemplarEntity = exemplarEntity[..^"RepositoryTests".Length];
        }

        if (string.IsNullOrWhiteSpace(exemplarEntity)
            || exemplarEntity.Equals(entityName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string template = File.ReadAllText(exemplarPath);
        string camelEntity = char.ToLowerInvariant(entityName[0]) + entityName[1..];
        string camelExemplar = char.ToLowerInvariant(exemplarEntity[0]) + exemplarEntity[1..];

        content = template
            .Replace($"I{exemplarEntity}Repository", $"I{entityName}Repository", StringComparison.Ordinal)
            .Replace($"{exemplarEntity}RepositoryTests", $"{entityName}RepositoryTests", StringComparison.Ordinal)
            .Replace($"new {exemplarEntity}(", $"new {entityName}(", StringComparison.Ordinal)
            .Replace($"var {camelExemplar}Repository", $"var {camelEntity}Repository", StringComparison.Ordinal)
            .Replace(exemplarEntity, entityName, StringComparison.Ordinal);

        return CSharpSyntaxGuard.TryValidate(content, out _);
    }
}
