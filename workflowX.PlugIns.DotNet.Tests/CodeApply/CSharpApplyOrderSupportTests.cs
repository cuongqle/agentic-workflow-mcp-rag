using workflowX.Infrastructure;
using workflowX.Infrastructure.CodeApply.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class CSharpApplyOrderSupportTests
{
    [Fact]
    public void OrderForApply_orders_interfaces_entities_layers_then_tests()
    {
        var conventions = new LayerConventionProfiles(
        [
            CreateProfile("Repository", "Repository.cs"),
            CreateProfile("Controller", "Controller.cs", ["ICompanyRepository"])
        ]);

        var files = new List<GeneratedFile>
        {
            File("Tests/CompanyControllerTests.cs"),
            File("Controllers/CompanyController.cs"),
            File("Repositories/CompanyRepository.cs"),
            File("Interfaces/ICompanyRepository.cs"),
            File("Entities/Company.cs")
        };

        var ordered = CSharpApplyOrderSupport.OrderForApply(files, conventions);

        Assert.Equal(
            [
                "Interfaces/ICompanyRepository.cs",
                "Entities/Company.cs",
                "Repositories/CompanyRepository.cs",
                "Controllers/CompanyController.cs",
                "Tests/CompanyControllerTests.cs"
            ],
            ordered.Select(f => f.RelativePath).ToList());
    }

    [Fact]
    public void OrderForApply_uses_entity_canonical_directory_from_contract()
    {
        var contract = new RepoContract
        {
            RepoPath = "/tmp",
            Entity = new EntityConvention(
                "Domain/Entities",
                "IEntity",
                "Domain/Entities/Sample.cs",
                null)
        };

        var files = new List<GeneratedFile>
        {
            File("Domain/Entities/Timesheet.cs"),
            File("Interfaces/ITimesheetRepository.cs")
        };

        var ordered = CSharpApplyOrderSupport.OrderForApply(files, LayerConventionProfiles.Empty, contract);

        Assert.Equal("Interfaces/ITimesheetRepository.cs", ordered[0].RelativePath);
        Assert.Equal("Domain/Entities/Timesheet.cs", ordered[1].RelativePath);
    }

    private static LayerConventionProfile CreateProfile(
        string roleName,
        string suffix,
        IReadOnlyList<string>? requiredCtorTypes = null) =>
        new(
            RoleName: roleName,
            FileSuffix: suffix,
            SampleCount: 2,
            CanonicalDirectory: null,
            RequireInheritanceClause: false,
            RequireMatchingRoleInterface: false,
            RequireBaseConstructorCall: false,
            RequiredInheritedTypeTokens: Array.Empty<string>(),
            RequiredConstructorParamTypes: requiredCtorTypes ?? Array.Empty<string>(),
            InterfacePairing: LayerInterfacePairingConvention.None);

    private static GeneratedFile File(string relativePath) =>
        new() { RelativePath = relativePath, Content = "// stub" };
}
