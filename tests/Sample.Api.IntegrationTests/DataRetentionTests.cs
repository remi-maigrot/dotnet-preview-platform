using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sample.Api.IntegrationTests.Infrastructure;
using Sample.Domain.Absences;
using Sample.Domain.Employees;
using Sample.Infrastructure.Configuration;
using Sample.Infrastructure.Retention;

namespace Sample.Api.IntegrationTests;

public sealed class DataRetentionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DataRetentionOptions Policy = new()
    {
        FreeTextRetention = TimeSpan.FromDays(180),
        RequestRetention = TimeSpan.FromDays(5 * 365),
    };

    private string _connectionString = string.Empty;
    private Employee _manager = null!;
    private Employee _employee = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateIsolatedDatabaseAsync();
        _manager = new Employee(Guid.NewGuid(), "Manager", "manager@example.test", null);
        _employee = new Employee(Guid.NewGuid(), "Alice", "alice@example.test", _manager.Id);
        await using var db = PostgresFixture.CreateDbContext(_connectionString);
        db.Employees.AddRange(_manager, _employee);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FreeText_IsErasedOnceTheDecisionIsOlderThanTheRetention()
    {
        var rejected = await SickLeaveAsync(daysAgo: 200, comment: "Flu, call me on 06 12 34 56 78", rejection: "Medical certificate missing");
        var recent = await SickLeaveAsync(daysAgo: 100, comment: "Back on Monday", rejection: null);

        var result = await RunAsync();

        result.FreeTextErased.ShouldBe(1);
        var erased = await LoadAsync(rejected);
        erased.ShouldNotBeNull();
        erased.Comment.ShouldBeNull();
        erased.RejectionReason.ShouldBeNull();
        erased.Status.ShouldBe(AbsenceStatus.Rejected, "the request itself is kept: only the free text goes");
        (await LoadAsync(recent))!.Comment.ShouldBe("Back on Monday");
    }

    [Fact]
    public async Task PendingRequests_KeepTheirCommentWhileTheyAreHandled()
    {
        var pending = await SickLeaveAsync(daysAgo: 200, comment: "Still waiting", rejection: null, decide: false);

        await RunAsync();

        (await LoadAsync(pending))!.Comment.ShouldBe("Still waiting");
    }

    [Fact]
    public async Task Requests_AreDeletedOnceTheAbsenceEndedLongerAgoThanTheRetention()
    {
        var old = await SickLeaveAsync(daysAgo: 6 * 365, comment: null, rejection: null);
        var kept = await SickLeaveAsync(daysAgo: 4 * 365, comment: null, rejection: null);

        var result = await RunAsync();

        result.RequestsDeleted.ShouldBe(1);
        (await LoadAsync(old)).ShouldBeNull();
        (await LoadAsync(kept)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ASecondRun_ChangesNothing()
    {
        await SickLeaveAsync(daysAgo: 200, comment: "Flu", rejection: null);
        await SickLeaveAsync(daysAgo: 6 * 365, comment: null, rejection: null);
        await RunAsync();

        var second = await RunAsync();

        second.FreeTextErased.ShouldBe(0);
        second.RequestsDeleted.ShouldBe(0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<RetentionResult> RunAsync()
    {
        await using var db = PostgresFixture.CreateDbContext(_connectionString);
        var purger = new DataRetentionPurger(db, Options.Create(Policy), NullLogger<DataRetentionPurger>.Instance);
        return await purger.RunAsync(Now, TestContext.Current.CancellationToken);
    }

    /// <summary>A sick leave of one week that ended <paramref name="daysAgo"/> days ago, decided the day it ended.</summary>
    private async Task<Guid> SickLeaveAsync(int daysAgo, string? comment, string? rejection, bool decide = true)
    {
        var end = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-daysAgo);
        var period = DateRange.Create(end.AddDays(-6), end).Value;
        var decidedAt = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var request = AbsenceRequest.Submit(Guid.CreateVersion7(), _employee, AbsenceType.SickLeave, period, comment,
            new HashSet<DateOnly>(), [], decidedAt).Value;
        if (decide)
        {
            var decision = rejection is null
                ? request.Approve(_employee, _manager.Id, decidedAt)
                : request.Reject(_employee, _manager.Id, rejection, decidedAt);
            decision.IsSuccess.ShouldBeTrue();
        }

        await using var db = PostgresFixture.CreateDbContext(_connectionString);
        db.AbsenceRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return request.Id;
    }

    private async Task<AbsenceRequest?> LoadAsync(Guid id)
    {
        await using var db = PostgresFixture.CreateDbContext(_connectionString);
        return await db.AbsenceRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }
}
