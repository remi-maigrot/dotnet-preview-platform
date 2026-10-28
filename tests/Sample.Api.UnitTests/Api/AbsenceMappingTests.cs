using Sample.Api.Endpoints;
using DomainModel = Sample.Domain.Absences;

namespace Sample.Api.UnitTests.Api;

public sealed class AbsenceMappingTests
{
    [Fact]
    public void EveryDomainType_RoundTripsThroughTheContract()
    {
        foreach (var type in Enum.GetValues<DomainModel.AbsenceType>())
        {
            type.ToContract().ToDomain().ShouldBe(type);
            type.ToContract().ToString().ShouldBe(type.ToString());
        }
    }

    [Fact]
    public void EveryDomainStatus_RoundTripsThroughTheContract()
    {
        foreach (var status in Enum.GetValues<DomainModel.AbsenceStatus>())
        {
            status.ToContract().ToDomain().ShouldBe(status);
            status.ToContract().ToString().ShouldBe(status.ToString());
        }
    }

    [Fact]
    public void UnknownValue_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((DomainModel.AbsenceType)42).ToContract());
        Should.Throw<ArgumentOutOfRangeException>(() => ((Contracts.AbsenceStatus)42).ToDomain());
    }
}
