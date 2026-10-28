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

    [Fact]
    public async Task Timestamps_AfterSaving_EqualWhatTheDatabaseReturns()
    {
        // PostgreSQL keeps microseconds; .NET clocks can have 100 ns ticks (they do on Linux).
        var team = await Team.CreateAsync(postgres);
        var submittedAt = new DateTimeOffset(2026, 3, 2, 9, 30, 15, TimeSpan.Zero).AddTicks(1_234_567);
        await using var db = postgres.CreateDbContext();
        var request = NewRequest(team.Alice, submittedAt);
        db.AbsenceRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var other = postgres.CreateDbContext();
        var loaded = await other.AbsenceRequests.SingleAsync(r => r.Id == request.Id, TestContext.Current.CancellationToken);

        request.SubmittedAt.ShouldBe(loaded.SubmittedAt);
        request.SubmittedAt.ShouldBe(submittedAt.AddTicks(-7));
    }

    private async Task<Guid> CreatePendingRequestAsync(Employee employee)
    {
        var request = NewRequest(employee, DateTimeOffset.UtcNow);

        await using var db = postgres.CreateDbContext();
        db.AbsenceRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return request.Id;
    }

    private static AbsenceRequest NewRequest(Employee employee, DateTimeOffset submittedAt)
    {
        var monday = Dates.Monday(20);
        return AbsenceRequest.Submit(
            Guid.CreateVersion7(),
            employee,
            AbsenceType.PaidLeave,
            DateRange.Create(monday, monday.AddDays(4)).Value,
            null,
            new HashSet<DateOnly>(),
            [],
            submittedAt).Value;
    }
}
