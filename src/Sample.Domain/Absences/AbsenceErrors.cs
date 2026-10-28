using Sample.Domain.Common;

namespace Sample.Domain.Absences;

/// <summary>Every business error the absence domain can produce. Codes are part of the public API contract.</summary>
public static class AbsenceErrors
{
    public const int MaxCommentLength = 500;
    public const int MaxCalendarDays = 90;

    public static readonly DomainError EndBeforeStart =
        DomainError.Validation("absence.end_before_start", "The end date must be on or after the start date.");

    public static readonly DomainError StartsInThePast =
        DomainError.Validation("absence.starts_in_the_past", "An absence cannot start in the past.");

    public static readonly DomainError TooLong =
        DomainError.Validation("absence.too_long", $"An absence cannot exceed {MaxCalendarDays} calendar days.");

    public static readonly DomainError NoWorkingDay =
        DomainError.Validation("absence.no_working_day", "The period does not contain any working day.");

    public static readonly DomainError CommentTooLong =
        DomainError.Validation("absence.comment_too_long", $"A comment cannot exceed {MaxCommentLength} characters.");

    public static readonly DomainError RejectionReasonRequired =
        DomainError.Validation("absence.rejection_reason_required", "A reason is required to reject a request.");

    public static readonly DomainError Overlapping =
        DomainError.Conflict("absence.overlapping", "The period overlaps another pending or approved absence.");

    public static readonly DomainError NotPending =
        DomainError.Conflict("absence.not_pending", "Only a submitted request can be approved or rejected.");

    public static readonly DomainError CannotBeCancelled =
        DomainError.Conflict("absence.cannot_be_cancelled", "Only pending requests or approved absences that have not started can be cancelled.");

    public static readonly DomainError SelfApproval =
        DomainError.Forbidden("absence.self_approval", "Nobody can decide on their own request.");

    public static readonly DomainError NotTheManager =
        DomainError.Forbidden("absence.not_the_manager", "Only the employee's manager can decide on this request.");

    public static readonly DomainError NotTheOwner =
        DomainError.Forbidden("absence.not_the_owner", "Only the employee who submitted the request can cancel it.");

    public static readonly DomainError NotFound =
        DomainError.NotFound("absence.not_found", "The absence request does not exist.");

    public static readonly DomainError ConcurrentUpdate =
        DomainError.Conflict("absence.concurrent_update", "The request was modified by someone else. Reload it and try again.");
}
