using System.ComponentModel.DataAnnotations;

namespace Sample.Api.Contracts;

public sealed record RejectAbsenceRequest([Required, StringLength(500, MinimumLength = 1)] string Reason);
