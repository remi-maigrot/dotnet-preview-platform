using Sample.Domain.Absences;

namespace Sample.Application.Abstractions;

public interface IPublicHolidayProvider
{
    Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(DateRange period, CancellationToken cancellationToken);
}
