using Sample.Application.Common;
using Sample.Domain.Absences;

namespace Sample.Application.Absences;

/// <param name="Viewer">Employees see their own requests; managers also see their direct reports' requests.</param>
/// <param name="Status">Optional status filter.</param>
/// <param name="EmployeeId">Optional employee filter, still restricted to what the viewer can see.</param>
public sealed record AbsenceRequestFilter(Viewer Viewer, AbsenceStatus? Status = null, Guid? EmployeeId = null);
