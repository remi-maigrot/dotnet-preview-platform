using Sample.Domain.Employees;

namespace Sample.Api.UnitTests.Domain;

public sealed class EmployeeTests
{
    [Fact]
    public void Constructor_OwnManager_Throws()
    {
        var id = Guid.NewGuid();

        Should.Throw<ArgumentException>(() => new Employee(id, "Ada", "ada@example.test", id));
    }

    [Theory]
    [InlineData("", "ada@example.test")]
    [InlineData("Ada", " ")]
    public void Constructor_MissingIdentity_Throws(string fullName, string email) =>
        Should.Throw<ArgumentException>(() => new Employee(Guid.NewGuid(), fullName, email, null));

    [Fact]
    public void Pseudonymise_ReplacesIdentifyingData()
    {
        var employee = new Employee(Guid.NewGuid(), "Ada", "ada@example.test", null);

        employee.Pseudonymise("Person 1", "person1@example.invalid");

        employee.FullName.ShouldBe("Person 1");
        employee.Email.ShouldBe("person1@example.invalid");
    }
}
