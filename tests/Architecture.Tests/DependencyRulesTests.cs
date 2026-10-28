using NetArchTest.Rules;

namespace Architecture.Tests;

/// <summary>
/// Endpoints → Application → Domain, with Infrastructure plugged in from the outside.
/// The compiler already forbids cycles between projects; these rules forbid the shortcuts it cannot see.
/// </summary>
public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_DependsOnNothingButTheBaseLibrary()
    {
        var result = Types.InAssembly(Layers.Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Sample.Application",
                "Sample.Infrastructure",
                "Sample.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "System.Net.Http",
                "Npgsql")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Domain_ReferencesOnlySystemAssemblies() =>
        Layers.Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ShouldAllBe(name => name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard");

    [Fact]
    public void Application_DoesNotKnowAboutDeliveryOrPersistence()
    {
        var result = Types.InAssembly(Layers.Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Sample.Infrastructure",
                "Sample.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "System.Net.Http",
                "Npgsql",
                "Polly")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnTheHttpLayer()
    {
        var result = Types.InAssembly(Layers.Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Sample.Api", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Contracts_AreIndependentOfTheImplementation()
    {
        var result = Types.InAssembly(Layers.Contracts)
            .ShouldNot()
            .HaveDependencyOnAny("Sample.Domain", "Sample.Application", "Sample.Infrastructure", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Endpoints_DoNotTalkToInfrastructureDirectly()
    {
        var result = Types.InAssembly(Layers.Api)
            .That().ResideInNamespace("Sample.Api.Endpoints")
            .ShouldNot()
            .HaveDependencyOnAny("Sample.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    internal static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
