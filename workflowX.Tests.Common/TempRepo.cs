namespace workflowX.Tests.Helpers;

internal sealed class TempRepo : IDisposable
{
    public string Path { get; }

    public TempRepo(string? name = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "workflowX-tests",
            name ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        string? directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test temp directories.
        }
    }
}
