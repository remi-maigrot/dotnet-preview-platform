using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sample.Api.UnitTests.TestSupport;
using Sample.Application.Absences;
using Sample.Application.Abstractions;
using Sample.Application.Common;
using Sample.Application.Diagnostics;
using Sample.Domain.Absences;
using Sample.Domain.Employees;

namespace Sample.Api.UnitTests.Application;

public sealed class ChangeAbsenceRequestHandlerTests : IDisposable
{
    private readonly IEmployeeRepository _employees = Substitute.For<IEmployeeRepository>();
    private readonly IAbsenceRequestRepository _requests = Substitute.For<IAbsenceRequestRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MetricCollector<long> _decided;
    private readonly Employee _manager = Build.Manager();
    private readonly Employee _employee;
    private readonly AbsenceRequest _request;
    private readonly Viewer _managerViewer;
    private readonly Viewer _employeeViewer;
    private readonly ChangeAbsenceRequestHandler _handler;

    public ChangeAbsenceRequestHandlerTests()
    {
        _employee = Build.EmployeeOf(_manager);
        _request = Build.PendingRequest(_employee);
        _managerViewer = new Viewer(_manager.Id, IsManager: true);
        _employeeViewer = new Viewer(_employee.Id, IsManager: false);

        _employees.FindAsync(_employee.Id, Arg.Any<CancellationToken>()).Returns(_employee);
        _requests.FindVisibleAsync(_request.Id, Arg.Any<Viewer>(), Arg.Any<CancellationToken>()).Returns(_request);
        _decided = new MetricCollector<long>(_meterFactory, AbsenceMetrics.MeterName, "absence_requests_decided");

        _handler = new ChangeAbsenceRequestHandler(
            _employees,
            _requests,
            _unitOfWork,
            new AbsenceMetrics(_meterFactory),
            new FakeTimeProvider(Build.Now),
            NullLogger<ChangeAbsenceRequestHandler>.Instance);
    }

    [Fact]
    public async Task Approve_ByManager_SavesAndCountsTheDecision()
    {
        var result = await _handler.ApproveAsync(_request.Id, _managerViewer, TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(AbsenceStatus.Approved);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _decided.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["absence.outcome"].ShouldBe("Approved");
    }

    [Fact]
    public async Task Reject_ByManager_StoresTheReason()
    {
        var result = await _handler.RejectAsync(_request.Id, _managerViewer, "Release week", TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(AbsenceStatus.Rejected);
        result.Value.RejectionReason.ShouldBe("Release week");
    }

    [Fact]
    public async Task Cancel_ByOwner_IsCancelled()
    {
        var result = await _handler.CancelAsync(_request.Id, _employeeViewer, TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(AbsenceStatus.Cancelled);
    }

    [Fact]
    public async Task Change_InvisibleRequest_IsNotFound()
    {
        var result = await _handler.ApproveAsync(Guid.NewGuid(), _managerViewer, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.NotFound);
    }

    [Fact]
    public async Task Change_RefusedTransition_PersistsNothing()
    {
        var result = await _handler.ApproveAsync(_request.Id, _employeeViewer, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.SelfApproval);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _decided.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Change_ConcurrentUpdate_IsReportedAsConflict()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var result = await _handler.ApproveAsync(_request.Id, _managerViewer, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.ConcurrentUpdate);
    }

    [Fact]
    public async Task Change_RequestOfDeletedEmployee_IsAnInvariantViolation()
    {
        _employees.FindAsync(_employee.Id, Arg.Any<CancellationToken>()).Returns((Employee?)null);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _handler.ApproveAsync(_request.Id, _managerViewer, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        _decided.Dispose();
        _meterFactory.Dispose();
    }
}
