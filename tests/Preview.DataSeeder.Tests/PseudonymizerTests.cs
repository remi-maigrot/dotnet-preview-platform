
namespace Preview.DataSeeder.Tests;

public sealed class PseudonymizerTests
{
    private static readonly byte[] Salt = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherSalt = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void References_StayConsistent()
    {
        var copy = new Pseudonymizer(Salt).Pseudonymize(PersonalDataTests.RealisticSource());

        var ids = copy.Employees.Select(e => e.Id).ToHashSet();
        copy.Employees.Where(e => e.ManagerId is not null).ShouldAllBe(e => ids.Contains(e.ManagerId!.Value));
        copy.AbsenceRequests.ShouldAllBe(r => ids.Contains(r.EmployeeId));
        copy.AbsenceRequests.Where(r => r.DecidedBy is not null).ShouldAllBe(r => ids.Contains(r.DecidedBy!.Value));
    }

    [Fact]
    public void SameSalt_SameOutput_OtherSalt_OtherIds()
    {
        var source = PersonalDataTests.RealisticSource();

        var first = new Pseudonymizer(Salt).Pseudonymize(source);
        var second = new Pseudonymizer(Salt).Pseudonymize(source);
        var other = new Pseudonymizer(OtherSalt).Pseudonymize(source);

        second.Employees.ShouldBe(first.Employees);
        other.Employees.Select(e => e.Id).ShouldNotContain(first.Employees[0].Id);
        first.Employees.Select(e => e.Id).ShouldNotContain(source.Employees[0].Id);
    }

    [Fact]
    public void Dates_AreShiftedPerPersonAndKeepTheirDuration()
    {
        var source = PersonalDataTests.RealisticSource();

        var copy = new Pseudonymizer(Salt).Pseudonymize(source);

        foreach (var (original, shifted) in source.AbsenceRequests.Zip(copy.AbsenceRequests))
        {
            var shift = shifted.StartDate.DayNumber - original.StartDate.DayNumber;
            Math.Abs(shift).ShouldBeLessThanOrEqualTo(45);
            (shifted.EndDate.DayNumber - shifted.StartDate.DayNumber).ShouldBe(original.EndDate.DayNumber - original.StartDate.DayNumber);
            (shifted.SubmittedAt - original.SubmittedAt).ShouldBe(TimeSpan.FromDays(shift));
        }
    }

    [Fact]
    public void Emails_CannotReachAnyone() =>
        new Pseudonymizer(Salt).Pseudonymize(PersonalDataTests.RealisticSource())
            .Employees.ShouldAllBe(e => e.Email.EndsWith("@example.invalid", StringComparison.Ordinal));

    [Fact]
    public void ShortSalt_IsRejected() => Should.Throw<ArgumentException>(() => new Pseudonymizer(new byte[16]));
}
