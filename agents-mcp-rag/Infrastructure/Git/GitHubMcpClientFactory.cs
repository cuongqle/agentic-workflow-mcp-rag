using ModelContextProtocol.Client;

namespace agents_mcp_rag.Infrastructure;

public static class GitHubMcpClientFactory
{
    public static async Task<McpClient> ConnectAsync(string githubPat, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("\n=== Step 3: Connecting to GitHub via MCP ===");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "npx",
            Arguments = new[] { "-y", "@modelcontextprotocol/server-github" },
            EnvironmentVariables = new Dictionary<string, string?>
            {
                { "GITHUB_PERSONAL_ACCESS_TOKEN", githubPat }
            }
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        Console.WriteLine("Successfully connected to the GitHub MCP Server.");
        return client;
    }
}
