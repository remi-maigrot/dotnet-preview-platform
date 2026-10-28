using NSubstitute;
using Sample.Api.UnitTests.TestSupport;
using Sample.Application.Abstractions;
using Sample.Application.Absences;
using Sample.Application.Common;
using Sample.Domain.Absences;

namespace Sample.Api.UnitTests.Application;

public sealed class AbsenceRequestQueriesTests
{
    private readonly IAbsenceRequestRepository _requests = Substitute.For<IAbsenceRequestRepository>();
    private readonly Viewer _viewer = new(Guid.NewGuid(), IsManager: false);

    [Fact]
    public async Task Get_InvisibleRequest_IsNotFound()
    {
        var result = await new AbsenceRequestQueries(_requests).GetAsync(Guid.NewGuid(), _viewer, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.NotFound);
    }

    [Fact]
    public async Task Get_VisibleRequest_ReturnsIt()
    {
        var request = Build.PendingRequest(Build.EmployeeOf(Build.Manager()));
        _requests.FindVisibleAsync(request.Id, _viewer, Arg.Any<CancellationToken>()).Returns(request);

        var result = await new AbsenceRequestQueries(_requests).GetAsync(request.Id, _viewer, TestContext.Current.CancellationToken);

        result.Value.ShouldBeSameAs(request);
    }

    [Fact]
    public async Task List_DelegatesFilterAndPage()
    {
        var filter = new AbsenceRequestFilter(_viewer, AbsenceStatus.Approved);
        var page = PageRequest.Create(2, 10).Value;
        var expected = new PagedResult<AbsenceRequest>([], 2, 10, 0);
        _requests.ListVisibleAsync(filter, page, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new AbsenceRequestQueries(_requests).ListAsync(filter, page, TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(expected);
    }
}
