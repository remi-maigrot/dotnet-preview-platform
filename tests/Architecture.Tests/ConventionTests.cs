using NetArchTest.Rules;

namespace Architecture.Tests;

public sealed class ConventionTests
{
    [Fact]
    public void DomainClasses_AreSealedOrAbstract()
    {
        var result = Types.InAssembly(Layers.Domain)
            .That().AreClasses().And().AreNotAbstract().And().AreNotStatic().And().DoNotHaveNameEndingWith("Attribute")
            .And().DoNotHaveName("Result")
            .Should().BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DependencyRulesTests.Describe(result));
    }

    [Fact]
    public void Handlers_AreSealedAndLiveInTheApplicationLayer()
    {
        var result = Types.InAssembly(Layers.Application)
            .That().HaveNameEndingWith("Handler")
            .Should().BeSealed().And().ResideInNamespaceStartingWith("Sample.Application")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DependencyRulesTests.Describe(result));
    }

    [Fact]
    public void InfrastructureAdapters_AreNotPublic()
    {
        var result = Types.InAssembly(Layers.Infrastructure)
            .That().ResideInNamespaceMatching(@"Sample\.Infrastructure\.(Persistence\.Repositories|Holidays|Idempotency)")
            .ShouldNot().BePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DependencyRulesTests.Describe(result));
    }

    [Fact]
    public void ApiTypes_AreNotPublic()
    {
        var result = Types.InAssembly(Layers.Api)
            .That().ResideInNamespaceStartingWith("Sample.Api")
            .And().DoNotHaveName("Program")
            .ShouldNot().BePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(DependencyRulesTests.Describe(result));
    }
}
