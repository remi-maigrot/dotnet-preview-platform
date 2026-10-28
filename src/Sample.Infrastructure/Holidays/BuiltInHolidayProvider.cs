using Sample.Application.Abstractions;
using Sample.Domain.Absences;

namespace Sample.Infrastructure.Holidays;

internal sealed class BuiltInHolidayProvider : IPublicHolidayProvider
{
    public Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(DateRange period, CancellationToken cancellationToken) =>
        Task.FromResult(FrenchPublicHolidays.Between(period));
}
