using Microsoft.EntityFrameworkCore;
using Sample.Api.IntegrationTests.Infrastructure;
using Sample.Application.Abstractions;
using Sample.Domain.Absences;
using Sample.Domain.Employees;
using Sample.Infrastructure.Persistence;

namespace Sample.Api.IntegrationTests;

public sealed class PersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Model_HasNoChangesMissingFromMigrations()
    {
        await using var db = postgres.CreateDbContext();

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "the EF model changed without a migration: run `dotnet ef migrations add <Name>`");
    }

    [Fact]
    public async Task SaveChanges_LostUpdate_IsDetectedThroughXmin()
    {
        var team = await Team.CreateAsync(postgres);
        var requestId = await CreatePendingRequestAsync(team.Alice);

        await using var first = postgres.CreateDbContext();
        await using var second = postgres.CreateDbContext();
        var seenByManager = await first.AbsenceRequests.SingleAsync(r => r.Id == requestId, TestContext.Current.CancellationToken);
        var seenByEmployee = await second.AbsenceRequests.SingleAsync(r => r.Id == requestId, TestContext.Current.CancellationToken);

        seenByEmployee.Cancel(team.Alice.Id, DateTimeOffset.UtcNow);
        await new EfUnitOfWork(second).SaveChangesAsync(TestContext.Current.CancellationToken);

        seenByManager.Approve(team.Alice, team.Manager.Id, DateTimeOffset.UtcNow);
        await Should.ThrowAsync<ConcurrencyConflictException>(() =>
            new EfUnitOfWork(first).SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AbsenceRequest_RoundTripsWithItsPeriod()
    {
        var team = await Team.CreateAsync(postgres);
        var requestId = await CreatePendingRequestAsync(team.Alice);

        await using var db = postgres.CreateDbContext();
        var loaded = await db.AbsenceRequests.SingleAsync(r => r.Id == requestId, TestContext.Current.CancellationToken);

        loaded.Period.CalendarDays.ShouldBe(5);
        loaded.Type.ShouldBe(AbsenceType.PaidLeave);
        loaded.Status.ShouldBe(AbsenceStatus.Submitted);
    }

    private async Task<Guid> CreatePendingRequestAsync(Employee employee)
    {
        var monday = Dates.Monday(20);
        var request = AbsenceRequest.Submit(
            Guid.CreateVersion7(),
            employee,
            AbsenceType.PaidLeave,
            DateRange.Create(monday, monday.AddDays(4)).Value,
            null,
            new HashSet<DateOnly>(),
            [],
            DateTimeOffset.UtcNow).Value;

        await using var db = postgres.CreateDbContext();
        db.AbsenceRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return request.Id;
    }
}
