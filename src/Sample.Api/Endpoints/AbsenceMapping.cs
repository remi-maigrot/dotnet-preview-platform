using Sample.Api.Contracts;
using DomainModel = Sample.Domain.Absences;

namespace Sample.Api.Endpoints;

/// <summary>Explicit mapping between the domain and the public contract, so renaming one never silently breaks the other.</summary>
internal static class AbsenceMapping
{
    public static AbsenceRequestResponse ToResponse(this DomainModel.AbsenceRequest request) => new(
        request.Id,
        request.EmployeeId,
        request.Type.ToContract(),
        request.Status.ToContract(),
        request.Period.Start,
        request.Period.End,
        request.WorkingDays,
        request.Comment,
        request.SubmittedAt,
        request.DecidedAt,
        request.DecidedBy,
        request.RejectionReason);

    public static AbsenceType ToContract(this DomainModel.AbsenceType type) => type switch
    {
        DomainModel.AbsenceType.PaidLeave => AbsenceType.PaidLeave,
        DomainModel.AbsenceType.CompensatoryTime => AbsenceType.CompensatoryTime,
        DomainModel.AbsenceType.SickLeave => AbsenceType.SickLeave,
        DomainModel.AbsenceType.UnpaidLeave => AbsenceType.UnpaidLeave,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static DomainModel.AbsenceType ToDomain(this AbsenceType type) => type switch
    {
        AbsenceType.PaidLeave => DomainModel.AbsenceType.PaidLeave,
        AbsenceType.CompensatoryTime => DomainModel.AbsenceType.CompensatoryTime,
        AbsenceType.SickLeave => DomainModel.AbsenceType.SickLeave,
        AbsenceType.UnpaidLeave => DomainModel.AbsenceType.UnpaidLeave,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static AbsenceStatus ToContract(this DomainModel.AbsenceStatus status) => status switch
    {
        DomainModel.AbsenceStatus.Submitted => AbsenceStatus.Submitted,
        DomainModel.AbsenceStatus.Approved => AbsenceStatus.Approved,
        DomainModel.AbsenceStatus.Rejected => AbsenceStatus.Rejected,
        DomainModel.AbsenceStatus.Cancelled => AbsenceStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static DomainModel.AbsenceStatus ToDomain(this AbsenceStatus status) => status switch
    {
        AbsenceStatus.Submitted => DomainModel.AbsenceStatus.Submitted,
        AbsenceStatus.Approved => DomainModel.AbsenceStatus.Approved,
        AbsenceStatus.Rejected => DomainModel.AbsenceStatus.Rejected,
        AbsenceStatus.Cancelled => DomainModel.AbsenceStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
