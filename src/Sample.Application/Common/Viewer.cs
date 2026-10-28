namespace Sample.Application.Common;

/// <summary>The authenticated employee on whose behalf a query runs.</summary>
public sealed record Viewer(Guid EmployeeId, bool IsManager);
