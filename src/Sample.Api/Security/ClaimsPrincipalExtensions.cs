using System.Security.Claims;
using Sample.Application.Common;

namespace Sample.Api.Security;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The employee behind the token. Endpoints calling this require an authenticated user.</summary>
    public static Viewer ToViewer(this ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue(Claims.Subject);
        if (!Guid.TryParse(subject, out var employeeId))
        {
            throw new InvalidOperationException("The token subject is not an employee id.");
        }

        return new Viewer(employeeId, user.IsInRole(Claims.ManagerRole));
    }
}
