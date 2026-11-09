using NetArchTest.Rules;
using Xunit;

namespace JobPilot.ArchitectureTests;

public class ArchitectureTests
{
    private const string DomainNamespace = "JobPilot.Domain";
    private const string ApplicationNamespace = "JobPilot.Application";
    private const string InfrastructureNamespace = "JobPilot.Infrastructure";
    private const string ApiNamespace = "JobPilot.Api";
    private const string AutomationWorkerNamespace = "JobPilot.AutomationWorker";

    [Fact]
    public void Domain_ShouldNotHaveDependencyOnOtherProjects()
    {
        var result = Types.InAssembly(typeof(Domain.Common.AuditableEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace, AutomationWorkerNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Domain must not depend on higher layers.");
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructureOrPresentation()
    {
        var applicationAssembly = typeof(Application.Abstractions.IUserProfileService).Assembly;
        var result = Types.InAssembly(applicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace, AutomationWorkerNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Application must not depend on Infrastructure or Presentation hosts.");
    }
}
