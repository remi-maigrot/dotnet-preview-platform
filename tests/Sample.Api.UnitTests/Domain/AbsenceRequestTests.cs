using Sample.Api.UnitTests.TestSupport;
using Sample.Domain.Absences;
using Sample.Domain.Common;
using Sample.Domain.Employees;

namespace Sample.Api.UnitTests.Domain;

public sealed class AbsenceRequestTests
{
    private readonly Employee _manager = Build.Manager();
    private readonly Employee _employee;

    public AbsenceRequestTests() => _employee = Build.EmployeeOf(_manager);

    [Fact]
    public void Submit_ValidRequest_IsPendingWithWorkingDays()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 3, 11) };

        var result = Submit(Build.Range("2026-03-09", "2026-03-15"), holidays: holidays, comment: "  Family trip  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(AbsenceStatus.Submitted);
        result.Value.WorkingDays.ShouldBe(4);
        result.Value.Comment.ShouldBe("Family trip");
        result.Value.SubmittedAt.ShouldBe(Build.Now);
    }

    [Fact]
    public void Submit_BlankComment_IsStoredAsNull() =>
        Submit(Build.Range("2026-03-09", "2026-03-09"), comment: "   ").Value.Comment.ShouldBeNull();

    [Fact]
    public void Submit_PaidLeaveInThePast_Fails() =>
        Submit(Build.Range("2026-03-02", "2026-03-03")).Error.ShouldBe(AbsenceErrors.StartsInThePast);

    [Fact]
    public void Submit_SickLeaveInThePast_IsAllowed() =>
        Submit(Build.Range("2026-03-02", "2026-03-03"), AbsenceType.SickLeave).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Submit_LongerThanLimit_Fails() =>
        Submit(Build.Range("2026-03-09", "2026-06-30")).Error.ShouldBe(AbsenceErrors.TooLong);

    [Fact]
    public void Submit_CommentTooLong_Fails() =>
        Submit(Build.Range("2026-03-09", "2026-03-09"), comment: new string('x', AbsenceErrors.MaxCommentLength + 1))
            .Error.ShouldBe(AbsenceErrors.CommentTooLong);

    [Fact]
    public void Submit_WeekendOnly_Fails() =>
        Submit(Build.Range("2026-03-14", "2026-03-15")).Error.ShouldBe(AbsenceErrors.NoWorkingDay);

    [Fact]
    public void Submit_OverlappingActiveAbsence_Fails() =>
        Submit(Build.Range("2026-03-09", "2026-03-13"), active: [Build.Range("2026-03-13", "2026-03-20")])
            .Error.ShouldBe(AbsenceErrors.Overlapping);

    [Fact]
    public void Approve_ByManager_IsApproved()
    {
        var request = Build.PendingRequest(_employee);

        var result = request.Approve(_employee, _manager.Id, Build.Now);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(AbsenceStatus.Approved);
        request.DecidedBy.ShouldBe(_manager.Id);
        request.DecidedAt.ShouldBe(Build.Now);
    }

    [Fact]
    public void Approve_ByTheEmployee_IsSelfApproval() =>
        Build.PendingRequest(_employee).Approve(_employee, _employee.Id, Build.Now).Error.ShouldBe(AbsenceErrors.SelfApproval);

    [Fact]
    public void Approve_ByAnotherManager_IsRefused() =>
        Build.PendingRequest(_employee).Approve(_employee, Guid.NewGuid(), Build.Now).Error.ShouldBe(AbsenceErrors.NotTheManager);

    [Fact]
    public void Approve_Twice_IsRefused()
    {
        var request = Build.PendingRequest(_employee);
        request.Approve(_employee, _manager.Id, Build.Now);

        request.Approve(_employee, _manager.Id, Build.Now).Error.ShouldBe(AbsenceErrors.NotPending);
    }

    [Fact]
    public void Approve_WithAnotherEmployee_IsAProgrammingError() =>
        Should.Throw<ArgumentException>(() =>
            Build.PendingRequest(_employee).Approve(Build.EmployeeOf(_manager), _manager.Id, Build.Now));

    [Fact]
    public void Reject_WithReason_IsRejected()
    {
        var request = Build.PendingRequest(_employee);

        request.Reject(_employee, _manager.Id, " Team offsite that week ", Build.Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(AbsenceStatus.Rejected);
        request.RejectionReason.ShouldBe("Team offsite that week");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_Fails(string reason) =>
        Build.PendingRequest(_employee).Reject(_employee, _manager.Id, reason, Build.Now)
            .Error.ShouldBe(AbsenceErrors.RejectionReasonRequired);

    [Fact]
    public void Reject_ReasonTooLong_Fails() =>
        Build.PendingRequest(_employee).Reject(_employee, _manager.Id, new string('x', 501), Build.Now)
            .Error.ShouldBe(AbsenceErrors.CommentTooLong);

    [Fact]
    public void Reject_ByAnotherManager_IsRefused() =>
        Build.PendingRequest(_employee).Reject(_employee, Guid.NewGuid(), "No", Build.Now)
            .Error.ShouldBe(AbsenceErrors.NotTheManager);

    [Fact]
    public void Cancel_PendingByOwner_IsCancelled()
    {
        var request = Build.PendingRequest(_employee);

        request.Cancel(_employee.Id, Build.Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(AbsenceStatus.Cancelled);
    }

    [Fact]
    public void Cancel_ApprovedNotStarted_IsCancelled()
    {
        var request = Build.PendingRequest(_employee);
        request.Approve(_employee, _manager.Id, Build.Now);

        request.Cancel(_employee.Id, Build.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Cancel_ApprovedAlreadyStarted_IsRefused()
    {
        var request = Build.PendingRequest(_employee);
        request.Approve(_employee, _manager.Id, Build.Now);

        request.Cancel(_employee.Id, Build.Now.AddDays(6)).Error.ShouldBe(AbsenceErrors.CannotBeCancelled);
    }

    [Fact]
    public void Cancel_Rejected_IsRefused()
    {
        var request = Build.PendingRequest(_employee);
        request.Reject(_employee, _manager.Id, "No", Build.Now);

        request.Cancel(_employee.Id, Build.Now).Error.ShouldBe(AbsenceErrors.CannotBeCancelled);
    }

    [Fact]
    public void Cancel_BySomeoneElse_IsRefused() =>
        Build.PendingRequest(_employee).Cancel(_manager.Id, Build.Now).Error.ShouldBe(AbsenceErrors.NotTheOwner);

    [Fact]
    public void ApproveAutomatically_Pending_IsApprovedWithoutDecider()
    {
        var request = Build.PendingRequest(_employee, AbsenceType.SickLeave);

        request.ApproveAutomatically(Build.Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(AbsenceStatus.Approved);
        request.DecidedBy.ShouldBeNull();
        request.ApproveAutomatically(Build.Now).Error.ShouldBe(AbsenceErrors.NotPending);
    }

    private Result<AbsenceRequest> Submit(
        DateRange period,
        AbsenceType type = AbsenceType.PaidLeave,
        string? comment = null,
        IReadOnlySet<DateOnly>? holidays = null,
        IEnumerable<DateRange>? active = null) =>
        AbsenceRequest.Submit(
            Guid.NewGuid(),
            _employee,
            type,
            period,
            comment,
            holidays ?? Build.NoHolidays,
            active ?? [],
            Build.Now);
}
