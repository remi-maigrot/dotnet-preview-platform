using System.ComponentModel.DataAnnotations;

namespace Sample.Api.Contracts;

/// <summary>Body of <c>POST /v1/absence-requests</c>. Business rules (overlaps, past dates…) are checked by the domain.</summary>
public sealed record SubmitAbsenceRequest(
    [Required] AbsenceType? Type,
    [Required] DateOnly? StartDate,
    [Required] DateOnly? EndDate,
    [MaxLength(500)] string? Comment);
