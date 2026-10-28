using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sample.Api.UnitTests.TestSupport;
using Sample.Application.Abstractions;
using Sample.Application.Absences;
using Sample.Application.Configuration;
using Sample.Application.Diagnostics;
using Sample.Domain.Absences;
using Sample.Domain.Employees;

namespace Sample.Api.UnitTests.Application;

public sealed class SubmitAbsenceRequestHandlerTests : IDisposable
{
    private readonly IEmployeeRepository _employees = Substitute.For<IEmployeeRepository>();
    private readonly IAbsenceRequestRepository _requests = Substitute.For<IAbsenceRequestRepository>();
    private readonly IPublicHolidayProvider _holidays = Substitute.For<IPublicHolidayProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FeatureFlagsOptions _features = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MetricCollector<long> _submitted;
    private readonly MetricCollector<long> _decided;
    private readonly Employee _employee = Build.EmployeeOf(Build.Manager());
    private readonly SubmitAbsenceRequestHandler _handler;

    public SubmitAbsenceRequestHandlerTests()
    {
        _employees.FindAsync(_employee.Id, Arg.Any<CancellationToken>()).Returns(_employee);
        _holidays.GetHolidaysAsync(Arg.Any<DateRange>(), Arg.Any<CancellationToken>()).Returns(Build.NoHolidays);
        _requests.GetActivePeriodsAsync(_employee.Id, Arg.Any<CancellationToken>()).Returns([]);

        var options = Substitute.For<IOptionsSnapshot<FeatureFlagsOptions>>();
        options.Value.Returns(_features);
        var metrics = new AbsenceMetrics(_meterFactory);
        _submitted = new MetricCollector<long>(_meterFactory, AbsenceMetrics.MeterName, "absence_requests_submitted");
        _decided = new MetricCollector<long>(_meterFactory, AbsenceMetrics.MeterName, "absence_requests_decided");

        _handler = new SubmitAbsenceRequestHandler(
            _employees,
            _requests,
            _holidays,
            _unitOfWork,
            options,
            metrics,
            new FakeTimeProvider(Build.Now),
            NullLogger<SubmitAbsenceRequestHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsAndCountsTheRequest()
    {
        var result = await _handler.HandleAsync(Command(AbsenceType.PaidLeave), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(AbsenceStatus.Submitted);
        _requests.Received(1).Add(result.Value);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _submitted.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["absence.type"].ShouldBe("PaidLeave");
        _decided.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_SickLeaveWithAutoApproval_IsApprovedImmediately()
    {
        _features.AutoApproveSickLeave = true;

        var result = await _handler.HandleAsync(Command(AbsenceType.SickLeave), TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(AbsenceStatus.Approved);
        _decided.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["absence.automatic"].ShouldBe(true);
    }

    [Fact]
    public async Task Handle_SickLeaveWithoutAutoApproval_WaitsForTheManager()
    {
        var result = await _handler.HandleAsync(Command(AbsenceType.SickLeave), TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(AbsenceStatus.Submitted);
    }

    [Fact]
    public async Task Handle_UnknownEmployee_IsForbidden()
    {
        var command = Command(AbsenceType.PaidLeave) with { EmployeeId = Guid.NewGuid() };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(EmployeeErrors.Unknown);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidPeriod_FailsWithoutLookingUpTheEmployee()
    {
        var command = Command(AbsenceType.PaidLeave) with { End = new DateOnly(2026, 3, 1) };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.EndBeforeStart);
        await _employees.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BusinessRuleViolation_PersistsNothing()
    {
        _requests.GetActivePeriodsAsync(_employee.Id, Arg.Any<CancellationToken>())
            .Returns([Build.Range("2026-03-10", "2026-03-10")]);

        var result = await _handler.HandleAsync(Command(AbsenceType.PaidLeave), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AbsenceErrors.Overlapping);
        _requests.DidNotReceive().Add(Arg.Any<AbsenceRequest>());
        _submitted.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _submitted.Dispose();
        _decided.Dispose();
        _meterFactory.Dispose();
    }

    private SubmitAbsenceRequestCommand Command(AbsenceType type) =>
        new(_employee.Id, type, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 13), null);
}
