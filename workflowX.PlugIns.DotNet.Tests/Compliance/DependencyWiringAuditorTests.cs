using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Compliance;

public class DependencyWiringAuditorTests
{
    [Fact]
    public void SanitizeBootstrapRegistrations_keeps_preexisting_protected_store_registration()
    {
        const string content = """
            using Microsoft.Extensions.DependencyInjection;

            namespace HotSpot.Tests.Bootstrapper;

            public static class TestBootstrapper
            {
                public static IServiceProvider Create()
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<IDbStore, InMemoryDbStore>();
                    return services.BuildServiceProvider();
                }
            }
            """;

        string repoPath = CreateRepoWithProtectedDbStoreInterface();
        string sanitized = DependencyWiringAuditor.SanitizeBootstrapRegistrations(content, repoPath);

        Assert.Contains("services.AddSingleton<IDbStore, InMemoryDbStore>();", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void IsAllowedNewRegistrationLine_rejects_concrete_only_and_preexisting_pairs()
    {
        var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Interfaces/ITimesheetRepository.cs",
            "Repositories/TimesheetRepository.cs"
        };

        Assert.False(DependencyWiringAuditor.IsAllowedNewRegistrationLine(
            "services.AddScoped<CompanyRepository>();",
            proposed,
            string.Empty));
        Assert.False(DependencyWiringAuditor.IsAllowedNewRegistrationLine(
            "services.AddScoped<IRepository, Repository>();",
            proposed,
            string.Empty));
        Assert.True(DependencyWiringAuditor.IsAllowedNewRegistrationLine(
            "services.AddScoped<ITimesheetRepository, TimesheetRepository>();",
            proposed,
            string.Empty));
    }

    [Fact]
    public void SanitizeBootstrapRegistrations_removes_disallowed_new_lines_but_keeps_original()
    {
        const string original = """
            var services = new ServiceCollection();
            services.AddScoped<RavenDbStore>();
            return services.BuildServiceProvider();
            """;

        const string merged = """
            var services = new ServiceCollection();
            services.AddScoped<RavenDbStore>();
            services.AddScoped<CompanyRepository>();
            services.AddScoped<ITimesheetRepository, TimesheetRepository>();
            return services.BuildServiceProvider();
            """;

        var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Interfaces/ITimesheetRepository.cs",
            "Repositories/TimesheetRepository.cs"
        };

        string sanitized = DependencyWiringAuditor.SanitizeBootstrapRegistrations(
            merged,
            string.Empty,
            proposed,
            original);

        Assert.Contains("services.AddScoped<RavenDbStore>();", sanitized, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<ITimesheetRepository, TimesheetRepository>();", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanyRepository", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeBootstrapRegistrations_with_workflow_scope_and_no_original_keeps_existing_lines()
    {
        const string existing = """
            var services = new ServiceCollection();
            services.AddSingleton<IDbStore, InMemoryDbStore>();
            services.AddScoped<ICompanyRepository, CompanyRepository>();
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
            return services.BuildServiceProvider();
            """;

        var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Interfaces/ITimesheetRepository.cs",
            "Repositories/TimesheetRepository.cs"
        };

        string repoPath = CreateRepoWithProtectedDbStoreInterface();
        string sanitized = DependencyWiringAuditor.SanitizeBootstrapRegistrations(
            existing,
            repoPath,
            proposed);

        Assert.Contains("services.AddSingleton<IDbStore, InMemoryDbStore>();", sanitized, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<ICompanyRepository, CompanyRepository>();", sanitized, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<IEmployeeRepository, EmployeeRepository>();", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyMissingRegistrations_supports_program_style_hub_without_servicecollection_block()
    {
        using var repo = new workflowX.Tests.Helpers.TempRepo();
        repo.WriteFile(
            "SinglePageSample/SinglePageSample.WebAPI/Program.cs",
            """
            using Microsoft.Extensions.DependencyInjection;

            namespace SinglePageSample.WebAPI;

            public static class Program
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<ICompanyRepository, CompanyRepository>();
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample/SinglePageSample.WebAPI/TestBootstrapper.cs",
            """
            using Microsoft.Extensions.DependencyInjection;

            namespace SinglePageSample.WebAPI;

            public static class TestBootstrapper
            {
                public static IServiceProvider Create()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<ICompanyRepository, CompanyRepository>();
                    return services.BuildServiceProvider();
                }
            }
            """);

        WorkflowState state = workflowX.Tests.Helpers.WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Implementing;
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile { RelativePath = "SinglePageSample/SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs", Content = "public interface ITimesheetRepository {}" },
                new GeneratedFile { RelativePath = "SinglePageSample/SinglePageSample.Repository/TimesheetRepository.cs", Content = "public class TimesheetRepository : ITimesheetRepository {}" }
            ]
        };

        IReadOnlyList<string> applied = DependencyWiringAuditor.ApplyMissingRegistrations(state);

        string program = File.ReadAllText(Path.Combine(repo.Path, "SinglePageSample/SinglePageSample.WebAPI/Program.cs"));
        string testBootstrapper = File.ReadAllText(Path.Combine(repo.Path, "SinglePageSample/SinglePageSample.WebAPI/TestBootstrapper.cs"));
        Assert.Contains("ITimesheetRepository", program, StringComparison.Ordinal);
        Assert.Contains("ITimesheetRepository", testBootstrapper, StringComparison.Ordinal);
        Assert.NotEmpty(applied);
    }

    private static string CreateRepoWithProtectedDbStoreInterface()
    {
        string root = Path.Combine(Path.GetTempPath(), $"workflowx-di-auditor-{Guid.NewGuid():N}");
        string dbDir = Path.Combine(root, "Sample", "Db");
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(
            Path.Combine(dbDir, "IDbStore.cs"),
            """
            namespace Sample.Db;

            public interface IDbStore
            {
            }
            """);
        return root;
    }
}
